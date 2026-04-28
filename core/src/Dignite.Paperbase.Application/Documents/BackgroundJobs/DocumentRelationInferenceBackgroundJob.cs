using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Rag;
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
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentRelationInferenceBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        DocumentRelationInferenceWorkflow workflow,
        IDocumentKnowledgeIndex knowledgeIndex,
        IDocumentRelationRepository relationRepository,
        IGuidGenerator guidGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _workflow = workflow;
        _knowledgeIndex = knowledgeIndex;
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
            if (!_knowledgeIndex.Capabilities.SupportsSearchSimilarDocuments)
            {
                await _pipelineRunManager.SkipAsync(document, run,
                    "Knowledge index does not support document-level similarity search.");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            // SearchSimilarDocumentsAsync uses document-level mean-pooled vectors, returns
            // one result per document (no deduplication needed), excludes the source document.
            var similarDocuments = await _knowledgeIndex.SearchSimilarDocumentsAsync(
                document.Id,
                document.TenantId,
                _aiOptions.RelationInferenceCandidateTopK);

            if (similarDocuments.Count == 0)
            {
                await _pipelineRunManager.SkipAsync(document, run,
                    "No document embedding found — embedding pipeline may not have run yet.");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var candidateDocIds = similarDocuments
                .Select(r => r.DocumentId)
                .ToList();

            var candidates = new List<RelationCandidate>();
            foreach (var candidateId in candidateDocIds)
            {
                var candidate = await _documentRepository.FindAsync(candidateId);
                if (candidate?.ExtractedText != null)
                {
                    var maxLen = _aiOptions.MaxRelationCandidateSummaryLength;
                    var summary = candidate.ExtractedText.Length > maxLen
                        ? candidate.ExtractedText[..maxLen]
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

            var sourceText = document.ExtractedText ?? string.Empty;
            var budget = _aiOptions.MaxRelationInferencePromptCharacters;
            var usedChars = sourceText.Length + candidates.Sum(c => c.Summary.Length);
            if (usedChars > budget)
            {
                var beforeDrop = candidates.Count;
                while (candidates.Count > 0 && usedChars > budget)
                {
                    var last = candidates[^1];
                    usedChars -= last.Summary.Length;
                    candidates.RemoveAt(candidates.Count - 1);
                }
                Logger.LogWarning(
                    "RelationInference prompt budget exceeded for document {DocumentId}: " +
                    "{Before} candidates → {After} after drop (budget {Budget} chars).",
                    document.Id, beforeDrop, candidates.Count, budget);
            }

            if (candidates.Count == 0)
            {
                await _pipelineRunManager.CompleteAsync(document, run);
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var inferred = await _workflow.RunAsync(
                document.Id, sourceText, candidates);

            var filtered = inferred
                .Where(r => r.Confidence >= _aiOptions.RelationInferenceMinConfidence)
                .ToList();

            foreach (var rel in filtered)
            {
                await _relationRepository.InsertAsync(new DocumentRelation(
                    _guidGenerator.Create(),
                    document.TenantId,
                    document.Id,
                    rel.TargetDocumentId,
                    rel.Description,
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
