using System.Threading.Tasks;
using Dignite.Vault.Extract.Abstractions.Documents;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;

namespace Dignite.Vault.Extract.Documents.Pipelines.Classification;

/// <summary>
/// Shared implementation of "apply a manual classification result to an already-loaded document" (#623):
/// extracted out of <c>DocumentAppService.ApplyManualClassificationAsync</c> (which backs the operator
/// Confirm / Reclassify endpoints) so the Parse-cascade branch for an upload-declared document type
/// (<c>DocumentParseBackgroundJob</c>) can complete the Classification pipeline stage identically —
/// same run bookkeeping, same transactional field-extraction cascade scheduling (#527 §8), same
/// <c>DocumentClassifiedEto</c> shape — without duplicating the sequence.
/// <para>
/// Deliberately does <b>not</b> call <c>IRepository.UpdateAsync</c> or map to a DTO: both callers already
/// hold the document inside their own unit-of-work / DTO-mapping flow, and doing either here would
/// either duplicate the save or force a dependency this project should not have (DTO mapping lives in
/// Application's AppService, not in a pipeline-internal helper consumed by a BackgroundJob too).
/// </para>
/// </summary>
public class ManualClassificationApplier : ITransientDependency
{
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly DocumentPipelineJobScheduler _pipelineJobScheduler;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IClock _clock;

    public ManualClassificationApplier(
        DocumentPipelineRunManager pipelineRunManager,
        DocumentPipelineJobScheduler pipelineJobScheduler,
        IDistributedEventBus distributedEventBus,
        IClock clock)
    {
        _pipelineRunManager = pipelineRunManager;
        _pipelineJobScheduler = pipelineJobScheduler;
        _distributedEventBus = distributedEventBus;
        _clock = clock;
    }

    /// <summary>
    /// Queues + begins a Classification run, schedules the cascade field-extraction run transactionally
    /// (#527 §8: BEFORE completing classification, so lifecycle derivation cannot see a premature Ready off
    /// a prior succeeded run), completes the Classification run as a manual classification (confidence
    /// pinned to 1.0), and publishes <c>DocumentClassifiedEto</c>. Byte-for-byte the same sequence
    /// <c>DocumentAppService.ApplyManualClassificationAsync</c> ran inline before #623.
    /// </summary>
    public virtual async Task ApplyAsync(Document document, DocumentType typeDef)
    {
        Check.NotNull(typeDef, nameof(typeDef));

        var run = await _pipelineRunManager.QueueAsync(document, VaultExtractPipelines.Classification);
        await _pipelineRunManager.BeginAsync(document, run);

        // #527 §8: create the cascade field-extraction run + enqueue its job BEFORE completing the (manual)
        // classification, so completion derivation sees a *pending* field-extraction key pipeline and cannot
        // derive a premature Ready off a prior succeeded run when reclassifying an already-processed document.
        // typeDef.TypeCode is forwarded as the stale-reclassify early-exit hint FieldExtractionService reads.
        await _pipelineJobScheduler.QueueAsync(
            document, VaultExtractPipelines.FieldExtraction, expectedEventTypeCode: typeDef.TypeCode);

        await _pipelineRunManager.CompleteManualClassificationAsync(document, run, typeDef);

        await _distributedEventBus.PublishAsync(
            new DocumentClassifiedEto
            {
                DocumentId = document.Id,
                TenantId = document.TenantId,
                EventTime = _clock.Now,
                DocumentTypeCode = typeDef.TypeCode,
                ClassificationConfidence = 1.0
            });
    }
}
