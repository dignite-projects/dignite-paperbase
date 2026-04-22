using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
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
    private readonly IDocumentClassifier _documentClassifier;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly DocumentTypeOptions _documentTypeOptions;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        IDocumentClassifier documentClassifier,
        IDistributedEventBus distributedEventBus,
        IOptions<DocumentTypeOptions> documentTypeOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _documentClassifier = documentClassifier;
        _distributedEventBus = distributedEventBus;
        _documentTypeOptions = documentTypeOptions.Value;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);
        await _documentRepository.UpdateAsync(document);

        try
        {
            var request = new ClassificationRequest
            {
                ExtractedText = document.ExtractedText ?? string.Empty,
                CandidateTypes = _documentTypeOptions.Types
                    .Select(t => new DocumentTypeHint
                    {
                        TypeCode = t.TypeCode,
                        DisplayName = t.DisplayName,
                        Keywords = t.MatchKeywords
                    }).ToList()
            };

            var result = await _documentClassifier.ClassifyAsync(request);

            string resultCode;
            if (!string.IsNullOrEmpty(result.TypeCode))
            {
                var typeDef = _documentTypeOptions.Types
                    .FirstOrDefault(t => t.TypeCode == result.TypeCode);
                var threshold = typeDef?.ConfidenceThreshold ?? 0.7;

                if (result.ConfidenceScore >= threshold)
                {
                    await _pipelineRunManager.CompleteClassificationAsync(
                        document, run, result.TypeCode, result.ConfidenceScore);
                    resultCode = "OK";

                    await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
                    {
                        DocumentId = document.Id,
                        TenantId = document.TenantId,
                        DocumentTypeCode = result.TypeCode,
                        ConfidenceScore = result.ConfidenceScore,
                        ExtractedText = document.ExtractedText
                    });
                }
                else
                {
                    resultCode = "LowConfidence";
                    await _pipelineRunManager.CompleteAsync(document, run, resultCode);
                }
            }
            else
            {
                resultCode = "LowConfidence";
                await _pipelineRunManager.CompleteAsync(document, run, resultCode);
            }
            await _documentRepository.UpdateAsync(document);
        }
        catch (Exception ex)
        {
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
    }
}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
}
