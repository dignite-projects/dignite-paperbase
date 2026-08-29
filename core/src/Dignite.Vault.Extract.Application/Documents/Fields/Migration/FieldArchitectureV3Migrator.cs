using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Vault.Extract.Documents.Fields.Migration;

/// <summary>
/// Migrates one layer's field data from architecture v2 to v3 (#561): <see cref="FieldDefinition"/> rows
/// become <see cref="Field"/> rows, and each document's <see cref="DocumentExtractedField"/> rows become
/// its <see cref="Document.FlexFields"/> bag.
/// <para>
/// <b>This lives in the module, not in a host's data folder, on purpose.</b> The <c>vault</c> Pro edition
/// hosts these same tables in its own DbContext and owns its own migrations, so a host-local script would
/// migrate one deployment and silently leave the other on the v2 layout — and nothing in either
/// repository's build or tests would notice. Both hosts run this same code instead.
/// </para>
/// <para>
/// <b>Additive and idempotent.</b> Nothing is deleted: the v2 rows stay authoritative until the cutover
/// retires them, which is what makes the rollback before that point "revert the code" rather than
/// "restore a backup". Re-running skips definitions already migrated (matched by id, which is preserved)
/// and documents whose bag is already populated, so an interrupted run resumes rather than duplicating.
/// </para>
/// <para>
/// <b>One layer per call.</b> Every query here runs under ABP's ambient <c>IMultiTenant</c> filter, and
/// this deliberately does not pierce it — see the security conventions in CLAUDE.md. A multi-tenant
/// deployment calls this once per layer inside <c>ICurrentTenant.Change(...)</c>; the returned result
/// reports which layer it actually migrated so a caller cannot mistake one layer's numbers for the whole
/// database.
/// </para>
/// </summary>
public class FieldArchitectureV3Migrator : ITransientDependency
{
    protected IFieldDefinitionRepository FieldDefinitionRepository { get; }

    protected IFieldRepository FieldRepository { get; }

    protected IDocumentRepository DocumentRepository { get; }

    protected IRepository<Document, Guid> DocumentGenericRepository { get; }

    protected IFlexFieldIndexManager<Document> IndexManager { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected ILogger<FieldArchitectureV3Migrator> Logger { get; }

    public FieldArchitectureV3Migrator(
        IFieldDefinitionRepository fieldDefinitionRepository,
        IFieldRepository fieldRepository,
        IDocumentRepository documentRepository,
        IRepository<Document, Guid> documentGenericRepository,
        IFlexFieldIndexManager<Document> indexManager,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<FieldArchitectureV3Migrator> logger)
    {
        FieldDefinitionRepository = fieldDefinitionRepository;
        FieldRepository = fieldRepository;
        DocumentRepository = documentRepository;
        DocumentGenericRepository = documentGenericRepository;
        IndexManager = indexManager;
        CurrentTenant = currentTenant;
        UnitOfWorkManager = unitOfWorkManager;
        Logger = logger;
    }

    /// <summary>
    /// Runs the migration for the current ambient layer and returns what it did.
    /// </summary>
    public virtual async Task<FieldArchitectureV3MigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        // Opens its own unit of work rather than assuming one. The caller that matters most is a host's
        // "--migrate-database" path, which runs outside any ambient unit of work, so without this the
        // very first repository call fails with "A DbContext can only be created inside a unit of work".
        // requiresNew: false so an ambient one is joined instead of nested when there is one - which is
        // how the tests call it.
        using var uow = UnitOfWorkManager.Begin(requiresNew: false);

        var result = await MigrateCoreAsync(cancellationToken);

        await uow.CompleteAsync(cancellationToken);
        return result;
    }

    protected virtual async Task<FieldArchitectureV3MigrationResult> MigrateCoreAsync(
        CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenant.Id;
        Logger.LogInformation(
            "Field architecture v3 migration starting for layer {Layer}.",
            tenantId?.ToString() ?? "host");

        var definitionsMigrated = await MigrateDefinitionsAsync(cancellationToken);
        var (documentsMigrated, valuesMigrated) = await MigrateValuesAsync(cancellationToken);

        // The derived index is never hand-migrated: re-deriving it from the bags is the kernel's own
        // designed path, and running it here doubles as the first real exercise of the rebuild that every
        // later field-type or searchability change will depend on.
        //
        // Runs unconditionally, including when this pass migrated nothing.
        //
        // An earlier version skipped it when nothing had moved, to keep a repeat run cheap. Running the
        // migration against a real database showed why that is wrong: the first attempt migrated the
        // definitions and bags and then failed at the rebuild, so the second attempt correctly found
        // nothing left to migrate - and skipped the very step that had not finished, leaving the query
        // index permanently empty while reporting success. The skip removed exactly the resumability the
        // idempotency exists to provide.
        //
        // Rebuilding is itself idempotent and this is a maintenance command, not a startup path, so the
        // saving was never worth a correctness condition that only bites after a partial failure.
        await IndexManager.RebuildAsync(cancellationToken);

        var result = new FieldArchitectureV3MigrationResult(
            tenantId, definitionsMigrated, documentsMigrated, valuesMigrated);

        Logger.LogInformation(
            "Field architecture v3 migration finished for layer {Layer}: {Definitions} field definitions, " +
            "{Documents} documents, {Values} field values, query index rebuilt.",
            tenantId?.ToString() ?? "host", definitionsMigrated, documentsMigrated, valuesMigrated);

        return result;
    }

    /// <summary>
    /// v2 definitions to v3, preserving ids. Ones already migrated are skipped, which is what lets an
    /// interrupted run resume.
    /// </summary>
    protected virtual async Task<int> MigrateDefinitionsAsync(CancellationToken cancellationToken)
    {
        var definitions = await FieldDefinitionRepository.GetListAsync(cancellationToken: cancellationToken);
        if (definitions.Count == 0)
        {
            return 0;
        }

        var existingIds = (await FieldRepository.GetListByIdsAsync(
                definitions.Select(d => d.Id), cancellationToken))
            .Select(f => f.Id)
            .ToHashSet();

        var migrated = new List<Field>();
        foreach (var definition in definitions.Where(d => !existingIds.Contains(d.Id)))
        {
            migrated.Add(FieldDefinitionToFieldMapper.Map(definition));
        }

        if (migrated.Count > 0)
        {
            await FieldRepository.InsertManyAsync(migrated, autoSave: true, cancellationToken);
        }

        return migrated.Count;
    }

    /// <summary>
    /// Each document's field-value rows to its bag.
    /// <para>
    /// Paged rather than loaded whole: documents carry <c>Markdown</c>, so materializing the corpus at
    /// once would be the one part of this migration whose memory cost scales with content rather than
    /// with field count.
    /// </para>
    /// </summary>
    protected virtual async Task<(int Documents, int Values)> MigrateValuesAsync(CancellationToken cancellationToken)
    {
        // Cached across the whole pass: definitions are few and every document needs them to resolve a
        // field id to its bag key.
        var fields = await FieldRepository.GetListAsync(cancellationToken: cancellationToken);
        if (fields.Count == 0)
        {
            return (0, 0);
        }

        var documentsMigrated = 0;
        var valuesMigrated = 0;
        var skip = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await DocumentGenericRepository.GetPagedListAsync(
                skip, BatchSize, sorting: nameof(Document.Id), includeDetails: false, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var summary in page)
            {
                // Reload with the field-value collection: the paged query deliberately does not include
                // details, and the child rows are exactly what this pass reads.
                var document = await DocumentRepository.FindWithFieldValuesAsync(summary.Id, cancellationToken);
                if (document == null || document.ExtractedFieldValues.Count == 0)
                {
                    continue;
                }

                // Already migrated - re-running must not rewrite a bag an operator may since have edited.
                if (document.FlexFields.Count > 0)
                {
                    continue;
                }

                var bag = FieldValueBagBuilder.Build(document.ExtractedFieldValues, fields);
                if (bag.Count == 0)
                {
                    continue;
                }

                foreach (var entry in bag)
                {
                    document.SetField(entry.Key, entry.Value);
                }

                await DocumentRepository.UpdateAsync(document, autoSave: true, cancellationToken);

                documentsMigrated++;
                valuesMigrated += bag.Count;
            }

            skip += page.Count;
        }

        return (documentsMigrated, valuesMigrated);
    }

    /// <summary>
    /// Recomputes <see cref="Document.FieldFingerprint"/> for every document in the current layer from the
    /// v3 value bag (#561 step 6).
    /// <para>
    /// <b>Deliberately not called by <see cref="MigrateAsync"/>. Run this at the cutover, once v3 owns
    /// extraction — never before.</b> Duplicate detection compares stored fingerprints by string
    /// equality, so it only needs every document hashed by the <i>same</i> calculator, not by any
    /// particular one. Running this during the additive phase would satisfy that for exactly as long as
    /// nothing is re-extracted: the v2 pipeline is still live and would write a v2-shaped fingerprint for
    /// the next document that passes through it, leaving a corpus split into two populations that can
    /// never match each other. Missed duplicates, silently.
    /// </para>
    /// <para>
    /// Call it immediately after the extraction path switches, in the same maintenance window. The
    /// acceptance check is that the number of documents carrying
    /// <see cref="DocumentReviewReasons.DuplicateSuspected"/> is unchanged across it.
    /// </para>
    /// </summary>
    public virtual async Task<int> RecomputeFingerprintsAsync(CancellationToken cancellationToken = default)
    {
        using var uow = UnitOfWorkManager.Begin(requiresNew: false);

        var recomputed = await RecomputeFingerprintsCoreAsync(cancellationToken);

        await uow.CompleteAsync(cancellationToken);
        return recomputed;
    }

    protected virtual async Task<int> RecomputeFingerprintsCoreAsync(CancellationToken cancellationToken)
    {
        var fieldsByType = new Dictionary<Guid, List<Field>>();
        var recomputed = 0;
        var skip = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await DocumentGenericRepository.GetPagedListAsync(
                skip, BatchSize, sorting: nameof(Document.Id), includeDetails: false, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var document in page)
            {
                if (document.DocumentTypeId == null)
                {
                    continue;
                }

                if (!fieldsByType.TryGetValue(document.DocumentTypeId.Value, out var fields))
                {
                    fields = await FieldRepository.GetListAsync(document.DocumentTypeId.Value, cancellationToken);
                    fieldsByType[document.DocumentTypeId.Value] = fields;
                }

                var fingerprint = FlexFieldFingerprintCalculator.Compute(document, fields);
                if (string.Equals(fingerprint, document.FieldFingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                document.SetFieldFingerprint(fingerprint);
                await DocumentGenericRepository.UpdateAsync(document, autoSave: true, cancellationToken);
                recomputed++;
            }

            skip += page.Count;
        }

        Logger.LogInformation(
            "Recomputed {Count} document fingerprints from the v3 value bag for layer {Layer}.",
            recomputed, CurrentTenant.Id?.ToString() ?? "host");

        return recomputed;
    }

    /// <summary>
    /// Deliberately modest. This deployment holds a few hundred documents, and the page size is a memory
    /// bound on rows that carry Markdown, not a throughput dial worth tuning.
    /// </summary>
    protected virtual int BatchSize => 50;
}

/// <summary>
/// What one <see cref="FieldArchitectureV3Migrator.MigrateAsync"/> call did.
/// <para>
/// <see cref="TenantId"/> is reported rather than assumed because the migrator handles exactly one layer
/// per call: without it, a multi-layer deployment could read one layer's counts as the whole database's.
/// </para>
/// </summary>
public sealed record FieldArchitectureV3MigrationResult(
    Guid? TenantId,
    int DefinitionsMigrated,
    int DocumentsMigrated,
    int FieldValuesMigrated);
