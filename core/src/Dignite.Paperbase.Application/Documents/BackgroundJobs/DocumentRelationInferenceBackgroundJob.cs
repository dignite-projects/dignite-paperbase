using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Domain.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentRelationInference")]
public class DocumentRelationInferenceBackgroundJob
    : AsyncBackgroundJob<DocumentRelationInferenceJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentRelationInferenceWorkflow _workflow;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentRelationInferenceBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentRelationInferenceWorkflow workflow,
        IDocumentChunkRepository chunkRepository,
        IDocumentRelationRepository relationRepository,
        IGuidGenerator guidGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _chunkRepository = chunkRepository;
        _relationRepository = relationRepository;
        _guidGenerator = guidGenerator;
        _aiOptions = aiOptions.Value;
    }

    public override async Task ExecuteAsync(DocumentRelationInferenceJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.RelationInference);
        await _documentRepository.UpdateAsync(document);

        try
        {
            var sourceChunks = await _chunkRepository.GetListByDocumentIdAsync(document.Id);
            if (sourceChunks.Count == 0)
            {
                await _pipelineRunManager.SkipAsync(document, run, "No chunks found.");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var firstVector = sourceChunks.First().EmbeddingVector.ToArray();
            var candidateChunks = await _chunkRepository.SearchByVectorAsync(
                firstVector, topK: _aiOptions.QaTopKChunks * 3);

            var candidateDocIds = candidateChunks
                .Select(c => c.DocumentId)
                .Where(id => id != document.Id)
                .Distinct()
                .Take(_aiOptions.QaTopKChunks)
                .ToList();

            if (candidateDocIds.Count == 0)
            {
                await _pipelineRunManager.CompleteAsync(document, run);
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var candidates = new List<RelationCandidate>();
            foreach (var candidateId in candidateDocIds)
            {
                var candidate = await _documentRepository.FindAsync(candidateId);
                if (candidate?.ExtractedText != null)
                {
                    var summary = candidate.ExtractedText.Length > 500
                        ? candidate.ExtractedText[..500]
                        : candidate.ExtractedText;

                    candidates.Add(new RelationCandidate
                    {
                        DocumentId = candidate.Id,
                        DocumentTypeCode = candidate.DocumentTypeCode,
                        Summary = summary
                    });
                }
            }

            if (candidates.Count == 0)
            {
                await _pipelineRunManager.CompleteAsync(document, run);
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var inferred = await _workflow.RunAsync(
                document.Id, document.ExtractedText ?? string.Empty, candidates);

            foreach (var rel in inferred)
            {
                await _relationRepository.InsertAsync(new DocumentRelation(
                    _guidGenerator.Create(),
                    document.TenantId,
                    document.Id,
                    rel.TargetDocumentId,
                    rel.RelationType,
                    RelationSource.AiSuggested,
                    rel.Confidence));
            }

            await _pipelineRunManager.CompleteAsync(document, run);
            await _documentRepository.UpdateAsync(document);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "RelationInference failed for document {DocumentId}. Lifecycle unchanged.",
                document.Id);
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
    }
}

public class DocumentRelationInferenceJobArgs
{
    public Guid DocumentId { get; set; }
}
