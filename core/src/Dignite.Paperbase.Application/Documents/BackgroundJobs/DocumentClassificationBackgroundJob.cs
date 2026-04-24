using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents.Classification;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Domain.Documents;
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
    private readonly IBackgroundJobManager _backgroundJobManager;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentClassificationWorkflow workflow,
        KeywordDocumentClassifier keywordClassifier,
        IDistributedEventBus distributedEventBus,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        IBackgroundJobManager backgroundJobManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _keywordClassifier = keywordClassifier;
        _distributedEventBus = distributedEventBus;
        _documentTypeOptions = documentTypeOptions.Value;
        _backgroundJobManager = backgroundJobManager;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);
        await _documentRepository.UpdateAsync(document);

        try
        {
            var candidates = _documentTypeOptions.Types
                .OrderByDescending(t => t.Priority)
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
                outcome = _keywordClassifier.Classify(document.ExtractedText ?? string.Empty);
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
        string? metadataJson = null;
        if (!string.IsNullOrEmpty(outcome.Reason))
        {
            metadataJson = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Reason"] = outcome.Reason
            });
        }

        if (!string.IsNullOrEmpty(outcome.TypeCode))
        {
            var typeDef = _documentTypeOptions.Types
                .FirstOrDefault(t => t.TypeCode == outcome.TypeCode);
            var threshold = typeDef?.ConfidenceThreshold ?? 0.7;

            if (outcome.ConfidenceScore >= threshold)
            {
                await _pipelineRunManager.CompleteClassificationAsync(
                    document, run, outcome.TypeCode, outcome.ConfidenceScore, metadataJson);

                await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
                {
                    DocumentId = document.Id,
                    TenantId = document.TenantId,
                    DocumentTypeCode = outcome.TypeCode,
                    ConfidenceScore = outcome.ConfidenceScore,
                    ExtractedText = document.ExtractedText
                });

                await _backgroundJobManager.EnqueueAsync(
                    new DocumentEmbeddingJobArgs { DocumentId = document.Id });
                return;
            }
        }

        await _pipelineRunManager.CompleteAsync(document, run, "LowConfidence", metadataJson);
        await _pipelineRunManager.MarkPendingReviewAsync(document);
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
