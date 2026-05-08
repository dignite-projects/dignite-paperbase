using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.Embedding;

[BackgroundJobName("Paperbase.DocumentEmbedding")]
public class DocumentEmbeddingBackgroundJob
    : AsyncBackgroundJob<DocumentEmbeddingJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentPipelineRunAccessor _pipelineRunAccessor;
    private readonly DocumentEmbeddingWorkflow _workflow;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DocumentEmbeddingBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineRunAccessor pipelineRunAccessor,
        DocumentEmbeddingWorkflow workflow,
        IDocumentKnowledgeIndex knowledgeIndex,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _pipelineRunAccessor = pipelineRunAccessor;
        _workflow = workflow;
        _knowledgeIndex = knowledgeIndex;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public override async Task ExecuteAsync(DocumentEmbeddingJobArgs args)
    {
        var workItem = await BeginRunAsync(args);
        if (workItem == null)
        {
            return;
        }

        try
        {
            var chunks = await _workflow.RunAsync(workItem.Markdown);

            // UpsertDocumentAsync handles whole-document replace in the knowledge index.
            // TenantId is copied explicitly from Document so the operation is safe in
            // Hangfire jobs where ABP's ambient ICurrentTenant is not set by the HTTP pipeline.
            await _knowledgeIndex.UpsertDocumentAsync(new DocumentVectorIndexUpdate
            {
                DocumentId = workItem.DocumentId,
                TenantId = workItem.TenantId,
                DocumentTypeCode = workItem.DocumentTypeCode,
                Chunks = chunks.Count > 0
                    ? chunks
                        .Select(c => new DocumentVectorRecord
                        {
                            ChunkIndex = c.ChunkIndex,
                            Text = c.ChunkText,
                            Vector = c.Vector,
                            PageNumber = null
                        })
                        .ToList()
                    : []
            });

            await CompleteRunAsync(workItem.DocumentId, workItem.RunId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Embedding failed for document {DocumentId}. Pipeline run marked failed, lifecycle unchanged.",
                workItem.DocumentId);
            await FailRunAsync(workItem.DocumentId, workItem.RunId, ex.Message);
        }
    }

    private async Task<EmbeddingWorkItem?> BeginRunAsync(DocumentEmbeddingJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(args.DocumentId, includeDetails: true);
        if (string.IsNullOrWhiteSpace(document.Markdown))
        {
            if (args.PipelineRunId.HasValue)
            {
                var pendingRun = document.GetRun(args.PipelineRunId.Value);
                if (pendingRun != null)
                {
                    await _pipelineRunManager.SkipAsync(document, pendingRun, "document markdown is empty");
                    await _documentRepository.UpdateAsync(document, autoSave: true);
                }
            }

            await uow.CompleteAsync();
            return null;
        }

        var run = await _pipelineRunAccessor.BeginOrStartAsync(
            document, args.PipelineRunId, PaperbasePipelines.Embedding);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();

        return new EmbeddingWorkItem(
            run.Id,
            document.Id,
            document.TenantId,
            document.DocumentTypeCode,
            document.Markdown);
    }

    private async Task CompleteRunAsync(Guid documentId, Guid runId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.Embedding);

        await _pipelineRunManager.CompleteAsync(document, run);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();
    }

    private async Task FailRunAsync(Guid documentId, Guid runId, string errorMessage)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var document = await _documentRepository.GetAsync(documentId, includeDetails: true);
        var run = document.GetRun(runId)
            ?? await _pipelineRunAccessor.BeginOrStartAsync(
                document, runId, PaperbasePipelines.Embedding);

        await _pipelineRunManager.FailAsync(document, run, errorMessage);
        await _documentRepository.UpdateAsync(document, autoSave: true);

        await uow.CompleteAsync();
    }

    private sealed record EmbeddingWorkItem(
        Guid RunId,
        Guid DocumentId,
        Guid? TenantId,
        string? DocumentTypeCode,
        string Markdown);
}

public class DocumentEmbeddingJobArgs
{
    public Guid DocumentId { get; set; }
    public Guid? PipelineRunId { get; set; }
}
