using System;
using System.IO;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentTextExtraction")]
public class DocumentTextExtractionBackgroundJob
    : AsyncBackgroundJob<DocumentTextExtractionJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly ITextExtractor _textExtractor;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public DocumentTextExtractionBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        ITextExtractor textExtractor,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IBackgroundJobManager backgroundJobManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _textExtractor = textExtractor;
        _blobContainer = blobContainer;
        _backgroundJobManager = backgroundJobManager;
    }

    public override async Task ExecuteAsync(DocumentTextExtractionJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.TextExtraction);
        await _documentRepository.UpdateAsync(document);

        try
        {
            var blobStream = await _blobContainer.GetAsync(document.OriginalFileBlobName);
            var ctx = new TextExtractionContext
            {
                ContentType = document.FileOrigin.ContentType,
                FileExtension = Path.GetExtension(document.FileOrigin.OriginalFileName ?? string.Empty),
                LanguageHints = { "ja", "en" }
            };

            var result = await _textExtractor.ExtractAsync(blobStream, ctx);

            var actualSourceType = result.UsedOcr ? SourceType.Physical : SourceType.Digital;
            await _pipelineRunManager.CompleteTextExtractionAsync(document, run, result.ExtractedText, actualSourceType);
            await _documentRepository.UpdateAsync(document);

            await _backgroundJobManager.EnqueueAsync(
                new DocumentClassificationJobArgs { DocumentId = document.Id });
        }
        catch (Exception ex)
        {
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
    }
}

public class DocumentTextExtractionJobArgs
{
    public Guid DocumentId { get; set; }
}
