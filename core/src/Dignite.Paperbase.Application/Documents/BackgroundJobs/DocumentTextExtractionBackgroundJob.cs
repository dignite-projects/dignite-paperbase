using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Domain.Documents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentTextExtraction")]
public class DocumentTextExtractionBackgroundJob
    : AsyncBackgroundJob<DocumentTextExtractionJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly ITextExtractor _textExtractor;

    public DocumentTextExtractionBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        ITextExtractor textExtractor)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _textExtractor = textExtractor;
    }

    public override async Task ExecuteAsync(DocumentTextExtractionJobArgs args)
    {
        // TODO: Implement in Slice 1
        // 1. Load document by args.DocumentId
        // 2. Start pipeline run via _pipelineRunManager.StartAsync(document, PaperbasePipelines.TextExtraction)
        // 3. Load blob stream from BlobStoring
        // 4. Call _textExtractor.ExtractAsync(stream, context)
        // 5. document.SetExtractedText(result.ExtractedText)
        // 6. _pipelineRunManager.CompleteAsync(document, run, metadata: result.Metadata)
        // 7. Enqueue classification job
        throw new NotImplementedException("DocumentTextExtractionBackgroundJob is implemented in Slice 1.");
    }
}

public class DocumentTextExtractionJobArgs
{
    public Guid DocumentId { get; set; }
}
