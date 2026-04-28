using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Rag;
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
    private readonly IDocumentVectorStore _vectorStore;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentEmbeddingBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentEmbeddingWorkflow workflow,
        IDocumentVectorStore vectorStore,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _vectorStore = vectorStore;
        _backgroundJobManager = backgroundJobManager;
        _guidGenerator = guidGenerator;
    }

    public override async Task ExecuteAsync(DocumentEmbeddingJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
            return;

        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Embedding);
        await _documentRepository.UpdateAsync(document);

        try
        {
            // Provider-neutral cleanup of any previous index for this document.
            // For pgvector this targets the same chunk table; for external providers
            // (Azure AI Search, Qdrant, etc.) this is the only way to purge stale records.
            await _vectorStore.DeleteByDocumentIdAsync(document.Id);

            var chunks = await _workflow.RunAsync(document.ExtractedText);

            if (chunks.Count > 0)
            {
                // TenantId is copied explicitly from Document.TenantId so the operation
                // is safe in Hangfire jobs / CLI tools where ABP's ambient ICurrentTenant
                // is not set by the HTTP pipeline.
                var records = chunks
                    .Select(c => new DocumentVectorRecord
                    {
                        Id = _guidGenerator.Create(),
                        TenantId = document.TenantId,
                        DocumentId = document.Id,
                        DocumentTypeCode = document.DocumentTypeCode,
                        ChunkIndex = c.ChunkIndex,
                        Text = c.ChunkText,
                        Vector = c.Vector,
                        Title = null,
                        PageNumber = null
                    })
                    .ToList();

                await _vectorStore.UpsertAsync(records);
            }

            await _pipelineRunManager.CompleteAsync(document, run);
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
            throw;
        }
    }
}

public class DocumentEmbeddingJobArgs
{
    public Guid DocumentId { get; set; }
}
