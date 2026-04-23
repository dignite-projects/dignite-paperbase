using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentRelationInference")]
public class DocumentRelationInferenceBackgroundJob
    : AsyncBackgroundJob<DocumentRelationInferenceJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IRelationInferrer _relationInferrer;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly IAiCostLedger _costLedger;
    private readonly IFeatureChecker _featureChecker;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentRelationInferenceBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        IRelationInferrer relationInferrer,
        IDocumentChunkRepository chunkRepository,
        IDocumentRelationRepository relationRepository,
        IAiCostLedger costLedger,
        IFeatureChecker featureChecker,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _relationInferrer = relationInferrer;
        _chunkRepository = chunkRepository;
        _relationRepository = relationRepository;
        _costLedger = costLedger;
        _featureChecker = featureChecker;
        _currentTenant = currentTenant;
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
            var budgetStr = await _featureChecker.GetOrNullAsync(PaperbaseAIFeatures.MonthlyBudgetUsd);
            var budget = decimal.TryParse(budgetStr, out var b) ? b : decimal.MaxValue;
            var used = await _costLedger.GetCurrentMonthUsageAsync(_currentTenant.Id);

            if (used >= budget)
            {
                await _pipelineRunManager.SkipAsync(document, run,
                    $"Monthly AI budget exceeded. Budget: ${budget:F2}, Used: ${used:F2}",
                    "BudgetExceeded");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var sourceChunks = await _chunkRepository.GetListByDocumentIdAsync(document.Id);
            if (sourceChunks.Count == 0)
            {
                await _pipelineRunManager.SkipAsync(document, run, "No chunks found.", "NoChunks");
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
                await _pipelineRunManager.CompleteAsync(document, run, "NoCandidates");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var candidates = new List<DocumentSummary>();
            foreach (var candidateId in candidateDocIds)
            {
                var candidate = await _documentRepository.FindAsync(candidateId);
                if (candidate?.ExtractedText != null)
                {
                    var summary = candidate.ExtractedText.Length > 500
                        ? candidate.ExtractedText[..500]
                        : candidate.ExtractedText;

                    candidates.Add(new DocumentSummary
                    {
                        DocumentId = candidate.Id,
                        DocumentTypeCode = candidate.DocumentTypeCode,
                        Summary = summary
                    });
                }
            }

            if (candidates.Count == 0)
            {
                await _pipelineRunManager.CompleteAsync(document, run, "NoCandidates");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var inferred = await _relationInferrer.InferAsync(new RelationInferenceRequest
            {
                DocumentId = document.Id,
                ExtractedText = document.ExtractedText ?? string.Empty,
                DocumentTypeCode = document.DocumentTypeCode,
                Candidates = candidates
            });

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

            await _pipelineRunManager.CompleteAsync(document, run, "OK");
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
