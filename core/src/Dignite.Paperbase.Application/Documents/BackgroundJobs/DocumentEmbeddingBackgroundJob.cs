using System;
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

[BackgroundJobName("Paperbase.DocumentEmbedding")]
public class DocumentEmbeddingBackgroundJob
    : AsyncBackgroundJob<DocumentEmbeddingJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IEmbeddingIndexer _embeddingIndexer;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IAiCostLedger _costLedger;
    private readonly IFeatureChecker _featureChecker;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentEmbeddingBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        IEmbeddingIndexer embeddingIndexer,
        IDocumentChunkRepository chunkRepository,
        IBackgroundJobManager backgroundJobManager,
        IAiCostLedger costLedger,
        IFeatureChecker featureChecker,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _embeddingIndexer = embeddingIndexer;
        _chunkRepository = chunkRepository;
        _backgroundJobManager = backgroundJobManager;
        _costLedger = costLedger;
        _featureChecker = featureChecker;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _aiOptions = aiOptions.Value;
    }

    public override async Task ExecuteAsync(DocumentEmbeddingJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Embedding);
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

            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                await _pipelineRunManager.SkipAsync(document, run, "No extracted text.", "NoText");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            await _chunkRepository.DeleteByDocumentIdAsync(document.Id);

            var indexResult = await _embeddingIndexer.IndexAsync(new EmbeddingIndexRequest
            {
                DocumentId = document.Id,
                DocumentTypeCode = document.DocumentTypeCode,
                ExtractedText = document.ExtractedText
            });

            foreach (var chunk in indexResult.Chunks)
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
