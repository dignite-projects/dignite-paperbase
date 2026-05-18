using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.TextExtraction;

[BackgroundJobName("Paperbase.DocumentTextExtraction")]
public class DocumentTextExtractionBackgroundJob
    : AsyncBackgroundJob<DocumentTextExtractionJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentPipelineRunAccessor _pipelineRunAccessor;
    private readonly DocumentPipelineJobScheduler _pipelineJobScheduler;
    private readonly ITextExtractor _textExtractor;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly OutboxEventManager _outboxEventManager;
    private readonly PaperbaseOcrOptions _ocrOptions;
    private readonly ISettingProvider _settingProvider;
    /// <summary>
    /// Document-title generation is single-shot, tool-free, prompt-unique. Reuses the
    /// host-registered <see cref="PaperbaseAIConsts.TitleGeneratorChatClientKey"/> client
    /// (no FunctionInvocation, no DistributedCache) so traces stay clean and hosts can
    /// optionally point title generation at a smaller / cheaper model.
    /// </summary>
    private readonly IChatClient _titleGeneratorChatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly PaperbaseAIBehaviorOptions _behaviorOptions;

    public DocumentTextExtractionBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineRunAccessor pipelineRunAccessor,
        DocumentPipelineJobScheduler pipelineJobScheduler,
        ITextExtractor textExtractor,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IUnitOfWorkManager unitOfWorkManager,
        OutboxEventManager outboxEventManager,
        IOptions<PaperbaseOcrOptions> ocrOptions,
        ISettingProvider settingProvider,
        [FromKeyedServices(PaperbaseAIConsts.TitleGeneratorChatClientKey)] IChatClient titleGeneratorChatClient,
        IPromptProvider promptProvider,
        IOptions<PaperbaseAIBehaviorOptions> behaviorOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _pipelineRunAccessor = pipelineRunAccessor;
        _pipelineJobScheduler = pipelineJobScheduler;
        _textExtractor = textExtractor;
        _blobContainer = blobContainer;
        _unitOfWorkManager = unitOfWorkManager;
        _outboxEventManager = outboxEventManager;
        _ocrOptions = ocrOptions.Value;
        _settingProvider = settingProvider;
        _titleGeneratorChatClient = titleGeneratorChatClient;
        _promptProvider = promptProvider;
        _behaviorOptions = behaviorOptions.Value;
    }

    public override async Task ExecuteAsync(DocumentTextExtractionJobArgs args)
    {
        var workItem = await BeginRunAsync(args);

        try
        {
            var blobStream = await _blobContainer.GetAsync(workItem.OriginalFileBlobName);
            var ctx = new TextExtractionContext
            {
                ContentType = workItem.ContentType,
                FileExtension = Path.GetExtension(workItem.OriginalFileName ?? string.Empty),
                LanguageHints = { "ja", "en" }
            };

            var result = await _textExtractor.ExtractAsync(blobStream, ctx);

            var actualSourceType = result.UsedOcr ? SourceType.Physical : SourceType.Digital;
            var title = await TryGenerateTitleAsync(result.Markdown)
                ?? MarkdownTitleExtractor.ExtractTitle(result.Markdown)
                ?? FallbackTitleFromFileName(workItem.OriginalFileName);
            await CompleteRunAsync(args.DocumentId, workItem.RunId, result, title, actualSourceType);
        }
        catch (Exception ex)
        {
            await FailRunAsync(args.DocumentId, workItem.RunId, ex.Message);
        }
    }

    private async Task<TextExtractionWorkItem> BeginRunAsync(DocumentTextExtractionJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(args.DocumentId, includeDetails: true);
        var run = await _pipelineRunAccessor.BeginOrStartAsync(
            document, args.PipelineRunId, PaperbasePipelines.TextExtraction);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();

        return new TextExtractionWorkItem(
            run.Id,
            document.OriginalFileBlobName,
            document.FileOrigin.ContentType,
            document.FileOrigin.OriginalFileName);
    }

    private async Task CompleteRunAsync(
        Guid documentId,
        Guid runId,
        TextExtractionResult result,
        string? title,
        SourceType actualSourceType)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.TextExtraction);

        await _pipelineRunManager.CompleteTextExtractionAsync(
            document, run, result.Markdown, title, actualSourceType);

        // 发布 OCRCompletedEto——薄载荷，下游通过 REST 回拉 Markdown。
        await _outboxEventManager.PublishAsync(
            document.TenantId,
            document.Id,
            new OCRCompletedEto
            {
                DocumentId = document.Id,
                TenantId = document.TenantId,
                OcrConfidence = result.Confidence,
                UsedOcr = result.UsedOcr
            });

        // OCR 置信度门槛检查（只对 UsedOcr=true 路径有意义；数字版抽取置信度默认 1.0 不会触发）
        var threshold = await ResolveOcrThresholdAsync(document.TenantId);
        if (result.UsedOcr && result.Confidence < threshold)
        {
            var reason = $"OCR confidence {result.Confidence:0.00} below threshold {threshold:0.00}";
            document.MarkPendingOcrReview(reason);
            await _documentRepository.UpdateAsync(document, autoSave: true);

            Logger.LogInformation(
                "Document {DocumentId} OCR confidence {Confidence:0.00} below threshold {Threshold:0.00}; routed to PendingReview.",
                document.Id, result.Confidence, threshold);
        }
        else
        {
            await _pipelineJobScheduler.QueueAsync(document, PaperbasePipelines.Classification);
        }

        await uow.CompleteAsync();
    }

    private async Task<double> ResolveOcrThresholdAsync(Guid? tenantId)
    {
        // per-tenant 覆盖（ABP Setting Management）优先；回落到部署级默认值。
        // ABP ISettingProvider 自动按 CurrentTenant 解析——即使 tenantId 显式传入也只用于
        // 日志/审计；ambient 上下文已经在调用方设置好。
        var settingValue = await _settingProvider.GetOrNullAsync(PaperbaseOcrOptions.ConfidenceThresholdSettingName);
        if (double.TryParse(settingValue, out var tenantThreshold))
        {
            return tenantThreshold;
        }
        return _ocrOptions.DefaultConfidenceThreshold;
    }

    private async Task<string?> TryGenerateTitleAsync(string markdown, CancellationToken cancellationToken = default)
    {
        try
        {
            var truncated = markdown.Length > _behaviorOptions.MaxTitleGenerationMarkdownLength
                ? markdown[.._behaviorOptions.MaxTitleGenerationMarkdownLength]
                : markdown;

            var template = _promptProvider.GetTitleGenerationPrompt(_behaviorOptions.DefaultLanguage);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, template.SystemInstructions),
                new(ChatRole.User, PromptBoundary.WrapDocument(truncated))
            };

            var response = await _titleGeneratorChatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var title = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(title))
                return null;

            return title.Length <= DocumentConsts.MaxTitleLength
                ? title
                : title[..DocumentConsts.MaxTitleLength];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "AI title generation failed; falling back to rule-based extractor.");
            return null;
        }
    }

    /// <summary>
    /// Markdown 标题抽取失败时的确定性回退：使用不带扩展名的原始文件名。
    /// 仍然为空（极端情况下 FileOrigin.OriginalFileName 为 null）则返回 null，让 UI 沿用原有文件名/blob 名展示。
    /// </summary>
    private static string? FallbackTitleFromFileName(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return null;
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(withoutExtension))
        {
            return null;
        }

        var trimmed = withoutExtension.Trim();
        return trimmed.Length <= DocumentConsts.MaxTitleLength
            ? trimmed
            : trimmed[..DocumentConsts.MaxTitleLength];
    }

    private async Task FailRunAsync(Guid documentId, Guid runId, string errorMessage)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.TextExtraction);

        await _pipelineRunManager.FailAsync(document, run, errorMessage);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();
    }

    private sealed record TextExtractionWorkItem(
        Guid RunId,
        string OriginalFileBlobName,
        string ContentType,
        string? OriginalFileName);
}

public class DocumentTextExtractionJobArgs
{
    public Guid DocumentId { get; set; }
    public Guid? PipelineRunId { get; set; }
}
