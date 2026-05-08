using System;
using System.IO;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Documents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
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

    public DocumentTextExtractionBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineRunAccessor pipelineRunAccessor,
        DocumentPipelineJobScheduler pipelineJobScheduler,
        ITextExtractor textExtractor,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _pipelineRunAccessor = pipelineRunAccessor;
        _pipelineJobScheduler = pipelineJobScheduler;
        _textExtractor = textExtractor;
        _blobContainer = blobContainer;
        _unitOfWorkManager = unitOfWorkManager;
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
            await CompleteRunAsync(args.DocumentId, workItem.RunId, result.Markdown, actualSourceType);
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
        string markdown,
        SourceType actualSourceType)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.TextExtraction);

        await _pipelineRunManager.CompleteTextExtractionAsync(
            document, run, markdown, actualSourceType);

        await _pipelineJobScheduler.QueueAsync(document, PaperbasePipelines.Classification);

        await uow.CompleteAsync();
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
