using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.Classification;

[BackgroundJobName("Paperbase.DocumentClassification")]
public class DocumentClassificationBackgroundJob
    : AsyncBackgroundJob<DocumentClassificationJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentPipelineRunAccessor _pipelineRunAccessor;
    private readonly DocumentClassificationWorkflow _workflow;
    private readonly OutboxEventManager _outboxEventManager;
    private readonly DocumentTypeOptions _documentTypeOptions;
    private readonly PaperbaseAIBehaviorOptions _aiOptions;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineRunAccessor pipelineRunAccessor,
        DocumentClassificationWorkflow workflow,
        OutboxEventManager outboxEventManager,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        IOptions<PaperbaseAIBehaviorOptions> aiOptions,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _pipelineRunAccessor = pipelineRunAccessor;
        _workflow = workflow;
        _outboxEventManager = outboxEventManager;
        _documentTypeOptions = documentTypeOptions.Value;
        _aiOptions = aiOptions.Value;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        var workItem = await BeginRunAsync(args);

        try
        {
            var outcome = await ClassifyAsync(workItem.DocumentId, workItem.Markdown);
            await CompleteRunAsync(workItem.DocumentId, workItem.RunId, outcome);
        }
        catch (Exception ex)
        {
            // 标记 Run 为 Failed 以保留运营侧可见性，并 rethrow 让 ABP BackgroundJob 框架按
            // MaxTryCount 重试（LLM transient 故障是典型重试场景）。args.PipelineRunId 在
            // 重试间稳定，下一次 BeginOrStartAsync 会复用同一 Run 并 MarkRunning 重置状态。
            await FailRunAsync(workItem.DocumentId, workItem.RunId, ex.Message);
            throw;
        }
    }

    private async Task<ClassificationWorkItem> BeginRunAsync(DocumentClassificationJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(args.DocumentId, includeDetails: true);
        var run = await _pipelineRunAccessor.BeginOrStartAsync(
            document, args.PipelineRunId, PaperbasePipelines.Classification);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();

        return new ClassificationWorkItem(run.Id, document.Id, document.Markdown ?? string.Empty);
    }

    private async Task<DocumentClassificationOutcome> ClassifyAsync(Guid documentId, string markdown)
    {
        // 候选集按 Priority 排序 + 截断，控制 prompt 体量。
        var candidates = _documentTypeOptions.Types
            .OrderByDescending(t => t.Priority)
            .Take(_aiOptions.MaxDocumentTypesInClassificationPrompt)
            .ToList();

        // LLM 路径直接吃 Markdown（结构信号有助于分类）。
        // transient 故障（网络/超时/取消）不在此处兜底——异常冒泡到 ExecuteAsync 走
        // FailRunAsync，由 ABP BackgroundJob 框架按 MaxTryCount 重试；LLM 恢复后下一次
        // 重试做完整 LLM 分类，避免低保真兜底把文档"提前结案"。
        try
        {
            return await _workflow.RunAsync(candidates, markdown);
        }
        catch (Exception ex) when (IsSchemaDeserializationError(ex))
        {
            // Schema 漂移：LLM 输出无法反序列化。重试也救不回来——直接走 PendingReview，
            // 由人工确认；避免把同一个坏输出反复重试空耗 quota。
            Logger.LogWarning(ex,
                "AI classification response failed JSON deserialization for document {DocumentId}; routing to PendingReview.",
                documentId);
            return new DocumentClassificationOutcome
            {
                TypeCode = null,
                ConfidenceScore = 0,
                Reason = "AI response could not be parsed (schema drift)."
            };
        }
    }

    private async Task CompleteRunAsync(
        Guid documentId,
        Guid runId,
        DocumentClassificationOutcome outcome)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.Classification);

        await ApplyClassificationResultAsync(document, run, outcome);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();
    }

    private async Task FailRunAsync(Guid documentId, Guid runId, string errorMessage)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.Classification);

        await _pipelineRunManager.FailAsync(document, run, errorMessage);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();
    }

    /// <summary>
    /// LLM 输出 JSON 反序列化失败（包括 SDK 包装的内层异常）。这类问题不可重试——
    /// schema 被破坏时再调一次 LLM 大概率还是同样的坏输出，应直接走 PendingReview。
    /// </summary>
    private static bool IsSchemaDeserializationError(Exception ex)
        => ex is JsonException || ex.GetBaseException() is JsonException;

    private async Task ApplyClassificationResultAsync(
        Document document,
        DocumentPipelineRun run,
        DocumentClassificationOutcome outcome)
    {
        var typeDef = string.IsNullOrEmpty(outcome.TypeCode)
            ? null
            : _documentTypeOptions.Types.FirstOrDefault(t => t.TypeCode == outcome.TypeCode);

        if (typeDef != null && outcome.ConfidenceScore >= typeDef.ConfidenceThreshold)
        {
            // 高置信度路径：ClassificationReason 由 ApplyAutomaticClassificationResult 固定置 null，
            // outcome.Reason 仅供低置信度路径使用，此处不传。
            await _pipelineRunManager.CompleteClassificationAsync(
                document, run, typeDef.TypeCode, outcome.ConfidenceScore);

            await _outboxEventManager.PublishAsync(
                document.TenantId,
                document.Id,
                new DocumentClassifiedEto
                {
                    DocumentId = document.Id,
                    TenantId = document.TenantId,
                    DocumentTypeCode = typeDef.TypeCode,
                    ClassificationConfidence = outcome.ConfidenceScore,
                    Markdown = document.Markdown
                });

            return;
        }

        var candidates = outcome.Candidates
            .Select(c => new PipelineRunCandidate(c.TypeCode, c.ConfidenceScore))
            .ToList();

        await _pipelineRunManager.CompleteClassificationWithLowConfidenceAsync(
            document, run, outcome.Reason, candidates);
    }

    private sealed record ClassificationWorkItem(
        Guid RunId,
        Guid DocumentId,
        string Markdown);
}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
    public Guid? PipelineRunId { get; set; }
}
