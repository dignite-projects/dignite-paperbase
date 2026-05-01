using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Pipelines.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Paperbase.Documents.Pipelines.Classification;

[BackgroundJobName("Paperbase.DocumentClassification")]
public class DocumentClassificationBackgroundJob
    : AsyncBackgroundJob<DocumentClassificationJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentClassificationWorkflow _workflow;
    private readonly KeywordDocumentClassifier _keywordClassifier;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly DocumentTypeOptions _documentTypeOptions;
    private readonly PaperbaseAIOptions _aiOptions;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentClassificationWorkflow workflow,
        KeywordDocumentClassifier keywordClassifier,
        IDistributedEventBus distributedEventBus,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        IOptions<PaperbaseAIOptions> aiOptions,
        IBackgroundJobManager backgroundJobManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _keywordClassifier = keywordClassifier;
        _distributedEventBus = distributedEventBus;
        _documentTypeOptions = documentTypeOptions.Value;
        _aiOptions = aiOptions.Value;
        _backgroundJobManager = backgroundJobManager;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);
        await _documentRepository.UpdateAsync(document);

        try
        {
            // 候选集在此处一次确定（按 Priority 排序 + 截断），同时供 LLM 路径与
            // 关键词兜底路径使用，避免两条路径结论指向不同子集。
            var candidates = _documentTypeOptions.Types
                .OrderByDescending(t => t.Priority)
                .Take(_aiOptions.MaxDocumentTypesInClassificationPrompt)
                .ToList();

            DocumentClassificationOutcome outcome;
            try
            {
                outcome = await _workflow.RunAsync(
                    candidates, document.ExtractedText ?? string.Empty);
            }
            catch (Exception ex) when (IsTransientProviderError(ex))
            {
                // 网络/超时类故障：LLM 暂时不可用，关键词兜底是合理替代——
                // 关键词命中即按兜底结果完成分类，未命中走 LowConfidence → PendingReview。
                Logger.LogWarning(ex,
                    "AI classification provider unavailable for document {DocumentId}; falling back to keyword classifier.",
                    document.Id);
                outcome = _keywordClassifier.Classify(candidates, document.ExtractedText ?? string.Empty);
            }
            catch (Exception ex) when (IsSchemaDeserializationError(ex))
            {
                // Schema 漂移：LLM 输出无法反序列化。这类问题不能用关键词兜底"修复"——
                // 关键词命中只反映文本表面特征，不能替代被破坏的 LLM 语义判定。
                // 直接走 PendingReview，由人工确认。
                Logger.LogWarning(ex,
                    "AI classification response failed JSON deserialization for document {DocumentId}; routing to PendingReview.",
                    document.Id);
                outcome = new DocumentClassificationOutcome
                {
                    TypeCode = null,
                    ConfidenceScore = 0,
                    Reason = "AI response could not be parsed (schema drift)."
                };
            }

            await ApplyClassificationResultAsync(document, run, outcome);
            await _documentRepository.UpdateAsync(document);
        }
        catch (Exception ex)
        {
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
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
            await _pipelineRunManager.CompleteClassificationAsync(
                document, run, typeDef.TypeCode, outcome.ConfidenceScore, outcome.Reason);

            await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
            {
                DocumentId = document.Id,
                TenantId = document.TenantId,
                DocumentTypeCode = typeDef.TypeCode,
                ClassificationConfidence = outcome.ConfidenceScore,
                ExtractedText = document.ExtractedText
            });

            await _backgroundJobManager.EnqueueAsync(
                new DocumentEmbeddingJobArgs { DocumentId = document.Id });
            return;
        }

        var candidates = outcome.Candidates
            .Select(c => new PipelineRunCandidate(c.TypeCode, c.ConfidenceScore))
            .ToList();

        await _pipelineRunManager.CompleteClassificationWithLowConfidenceAsync(
            document, run, outcome.Reason, candidates);
    }

}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
}
