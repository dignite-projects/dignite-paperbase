using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents.Classification;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

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
            catch (Exception ex) when (IsAiProviderError(ex))
            {
                Logger.LogWarning(ex,
                    "AI classification workflow failed for document {DocumentId}, falling back to keyword classifier.",
                    document.Id);
                outcome = _keywordClassifier.Classify(candidates, document.ExtractedText ?? string.Empty);
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

    private static bool IsAiProviderError(Exception ex)
    {
        return ex is TimeoutException
            || ex is OperationCanceledException
            || ex.GetType().Name.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase)
            || (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }
}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
}
