using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.Domain.Documents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentClassification")]
public class DocumentClassificationBackgroundJob
    : AsyncBackgroundJob<DocumentClassificationJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IDocumentClassifier _documentClassifier;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        IDocumentClassifier documentClassifier)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _documentClassifier = documentClassifier;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        // TODO: Implement in Slice 1
        // 1. Load document by args.DocumentId
        // 2. Start pipeline run via _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification)
        // 3. Build ClassificationRequest from document.ExtractedText + registered DocumentTypeOptions
        // 4. Call _documentClassifier.ClassifyAsync(request)
        // 5. If confidence >= threshold: document.SetClassificationResult() + _pipelineRunManager.CompleteAsync()
        //    + publish DocumentClassifiedEto
        // 6. If LowConfidence: run still Succeeded with ResultCode = "LowConfidence", no event published
        throw new NotImplementedException("DocumentClassificationBackgroundJob is implemented in Slice 1.");
    }
}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
}
