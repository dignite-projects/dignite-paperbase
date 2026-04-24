using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Domain.Documents;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentEmbedding")]
public class DocumentEmbeddingBackgroundJob
    : AsyncBackgroundJob<DocumentEmbeddingJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentEmbeddingWorkflow _workflow;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentEmbeddingBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentEmbeddingWorkflow workflow,
        IDocumentChunkRepository chunkRepository,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _chunkRepository = chunkRepository;
        _backgroundJobManager = backgroundJobManager;
        _guidGenerator = guidGenerator;
    }

    public override async Task ExecuteAsync(DocumentEmbeddingJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Embedding);
        await _documentRepository.UpdateAsync(document);

        try
        {
            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                await _pipelineRunManager.SkipAsync(document, run, "No extracted text.", "NoText");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            await _chunkRepository.DeleteByDocumentIdAsync(document.Id);

            var chunks = await _workflow.RunAsync(document.ExtractedText);

            foreach (var chunk in chunks)
            {
                await _chunkRepository.InsertAsync(new DocumentChunk(
                    _guidGenerator.Create(),
                    document.TenantId,
                    document.Id,
                    chunk.ChunkIndex,
                    chunk.ChunkText,
                    chunk.Vector));
            }

            await _pipelineRunManager.CompleteAsync(document, run, "OK");
            await _documentRepository.UpdateAsync(document);

            await _backgroundJobManager.EnqueueAsync(
                new DocumentRelationInferenceJobArgs { DocumentId = document.Id });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Embedding failed for document {DocumentId}. Pipeline run marked failed, lifecycle unchanged.",
                document.Id);
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
    }
}

public class DocumentEmbeddingJobArgs
{
    public Guid DocumentId { get; set; }
}
