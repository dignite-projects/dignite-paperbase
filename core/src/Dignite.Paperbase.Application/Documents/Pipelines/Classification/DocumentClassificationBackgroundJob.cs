using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.Classification;

[BackgroundJobName("Paperbase.DocumentClassification")]
public class DocumentClassificationBackgroundJob
    : AsyncBackgroundJob<DocumentClassificationJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentPipelineRunAccessor _pipelineRunAccessor;
    private readonly DocumentPipelineJobScheduler _pipelineJobScheduler;
    private readonly DocumentClassificationWorkflow _workflow;
    private readonly KeywordDocumentClassifier _keywordClassifier;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly DocumentTypeOptions _documentTypeOptions;
    private readonly PaperbaseAIBehaviorOptions _aiOptions;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineRunAccessor pipelineRunAccessor,
        DocumentPipelineJobScheduler pipelineJobScheduler,
        DocumentClassificationWorkflow workflow,
        KeywordDocumentClassifier keywordClassifier,
        IDistributedEventBus distributedEventBus,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        IOptions<PaperbaseAIBehaviorOptions> aiOptions,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _pipelineRunAccessor = pipelineRunAccessor;
        _pipelineJobScheduler = pipelineJobScheduler;
        _workflow = workflow;
        _keywordClassifier = keywordClassifier;
        _distributedEventBus = distributedEventBus;
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
            await FailRunAsync(workItem.DocumentId, workItem.RunId, ex.Message);
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
        // 候选集在此处一次确定（按 Priority 排序 + 截断），同时供 LLM 路径与
        // 关键词兜底路径使用，避免两条路径结论指向不同子集。
        var candidates = _documentTypeOptions.Types
            .OrderByDescending(t => t.Priority)
            .Take(_aiOptions.MaxDocumentTypesInClassificationPrompt)
            .ToList();

        // LLM 路径直接吃 Markdown（结构信号有助于分类）；
        // 关键词兜底走纯文本投影（关键词只匹配字面，结构标记是噪音）。
        try
        {
            return await _workflow.RunAsync(candidates, markdown);
        }
        catch (Exception ex) when (IsTransientProviderError(ex))
        {
            // 网络/超时类故障：LLM 暂时不可用，关键词兜底是合理替代——
            // 关键词命中即按兜底结果完成分类，未命中走 LowConfidence → PendingReview。
            Logger.LogWarning(ex,
                "AI classification provider unavailable for document {DocumentId}; falling back to keyword classifier.",
                documentId);
            return _keywordClassifier.Classify(candidates, MarkdownStripper.Strip(markdown));
        }
        catch (Exception ex) when (IsSchemaDeserializationError(ex))
        {
            // Schema 漂移：LLM 输出无法反序列化。这类问题不能用关键词兜底"修复"——
            // 关键词命中只反映文本表面特征，不能替代被破坏的 LLM 语义判定。
            // 直接走 PendingReview，由人工确认。
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

        var shouldQueueEmbedding = await ApplyClassificationResultAsync(document, run, outcome);
        if (shouldQueueEmbedding)
        {
            await _pipelineJobScheduler.QueueAsync(document, PaperbasePipelines.Embedding);
        }
        else
        {
            await _documentRepository.UpdateAsync(document, autoSave: true);
        }

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
    /// 网络/超时类瞬时故障：HTTP 通信失败、超时、操作取消。这类 LLM 不可用
    /// 的情形下，关键词兜底是合理替代，因为产品语义上"分类未必基于深层语义
    /// 理解"——关键词命中可信度足以推动流水线。
    /// </summary>
    private static bool IsTransientProviderError(Exception ex)
        => ex is HttpRequestException
            || ex is TimeoutException
            || ex is OperationCanceledException
            || ex.GetBaseException() is HttpRequestException
            || ex.GetBaseException() is TimeoutException;

    /// <summary>
    /// LLM 输出 JSON 反序列化失败（包括 SDK 包装的内层异常）。这类问题
    /// 必须走 PendingReview——schema 被破坏时关键词兜底无法替代语义判定。
    /// </summary>
    private static bool IsSchemaDeserializationError(Exception ex)
        => ex is JsonException || ex.GetBaseException() is JsonException;

    private async Task<bool> ApplyClassificationResultAsync(
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

            await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
            {
                DocumentId = document.Id,
                TenantId = document.TenantId,
                DocumentTypeCode = typeDef.TypeCode,
                ClassificationConfidence = outcome.ConfidenceScore,
                Markdown = document.Markdown
            });

            return true;
        }

        var candidates = outcome.Candidates
            .Select(c => new PipelineRunCandidate(c.TypeCode, c.ConfidenceScore))
            .ToList();

        await _pipelineRunManager.CompleteClassificationWithLowConfidenceAsync(
            document, run, outcome.Reason, candidates);
        return false;
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
