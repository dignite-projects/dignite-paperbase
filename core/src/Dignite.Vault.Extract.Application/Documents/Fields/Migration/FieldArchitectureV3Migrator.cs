using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
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

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    protected IDataFilter DataFilter { get; }

    protected ILogger<FieldArchitectureV3Migrator> Logger { get; }

    public FieldArchitectureV3Migrator(
        IFieldDefinitionRepository fieldDefinitionRepository,
        IFieldRepository fieldRepository,
        IDocumentRepository documentRepository,
        IRepository<Document, Guid> documentGenericRepository,
        IFlexFieldIndexManager<Document> indexManager,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ILogger<FieldArchitectureV3Migrator> logger)
    {
        FieldDefinitionRepository = fieldDefinitionRepository;
        FieldRepository = fieldRepository;
        DocumentRepository = documentRepository;
        DocumentGenericRepository = documentGenericRepository;
        IndexManager = indexManager;
        CurrentTenant = currentTenant;
        UnitOfWorkManager = unitOfWorkManager;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
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
    /// <para>
    /// Runs under <see cref="IDataFilter.Disable{TFilter}"/> for <see cref="ISoftDelete"/> on both the
    /// source read and the already-migrated check: a field the operator archived before the cutover is
    /// still a real row in <c>Field</c>'s own recycle bin (<c>FieldDefinitionAppService</c>'s
    /// <c>OnlyDeleted</c> view, <c>RestoreAsync</c>) and in historical documents' value bags
    /// (<c>FieldValueBagBuilder</c> resolves field names under the same filter disable) - silently
    /// skipping it here would make both permanently unreachable, not merely delayed.
    /// </para>
    /// </summary>
    protected virtual async Task<int> MigrateDefinitionsAsync(CancellationToken cancellationToken)
    {
        List<FieldDefinition> definitions;
        HashSet<Guid> existingIds;
        using (DataFilter.Disable<ISoftDelete>())
        {
            definitions = await FieldDefinitionRepository.GetListAsync(cancellationToken: cancellationToken);
            if (definitions.Count == 0)
            {
                return 0;
            }

            existingIds = (await FieldRepository.GetListByIdsAsync(
                    definitions.Select(d => d.Id), cancellationToken))
                .Select(f => f.Id)
                .ToHashSet();
        }

        var migrated = new List<Field>();
        foreach (var definition in definitions.Where(d => !existingIds.Contains(d.Id)))
        {
            var field = FieldDefinitionToFieldMapper.Map(definition);
            if (definition.IsDeleted)
            {
                field.IsDeleted = true;
                field.DeletionTime = definition.DeletionTime;
                field.DeleterId = definition.DeleterId;
            }

            migrated.Add(field);
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
    /// Paged, and paged over <b>ids</b> rather than entities: documents carry <c>Markdown</c>, so a page of
    /// full entities would load every body twice — once to read its id, once again in the reload that
    /// actually needs the child rows. The id projection makes the heavy load happen exactly once per
    /// document that has something to migrate.
    /// </para>
    /// </summary>
    protected virtual async Task<(int Documents, int Values)> MigrateValuesAsync(CancellationToken cancellationToken)
    {
        // Disabled for the whole method, the same reason MigrateDefinitionsAsync disables it: a field
        // archived before cutover must still resolve below (both in the definitions list and inside
        // FieldValueBagBuilder.Build), and a document soft-deleted before cutover must still be found and
        // loaded - otherwise its historical values are silently dropped, and once the v2 tables are
        // dropped that is permanent. Flows through PageDocumentIdsAsync and FindWithFieldValuesAsync via
        // ABP's ambient filter state, so neither needs its own disable.
        using (DataFilter.Disable<ISoftDelete>())
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

                var page = await PageDocumentIdsAsync(skip, cancellationToken);

                if (page.Count == 0)
                {
                    break;
                }

                foreach (var documentId in page)
                {
                    var document = await DocumentRepository.FindWithFieldValuesAsync(documentId, cancellationToken);
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

                    // autoSave: false - one flush per page below, not one per document. Each flush is its own
                    // round trip through ABP's audit and event plumbing, and nothing in this loop reads back
                    // what an earlier iteration wrote.
                    await DocumentRepository.UpdateAsync(document, autoSave: false, cancellationToken);

                    documentsMigrated++;
                    valuesMigrated += bag.Count;
                }

                await UnitOfWorkManager.Current!.SaveChangesAsync(cancellationToken);

                skip += page.Count;
            }

            return (documentsMigrated, valuesMigrated);
        }
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

            // Disabled for this call only, so a document soft-deleted before cutover is still found and
            // recomputed - otherwise restoring it later leaves a stale v2-shaped fingerprint that can never
            // match an otherwise-identical live document again (#411 compares fingerprints by plain string
            // equality). Scoped to just the page fetch: the per-type field lookup below must stay under the
            // default filter. Widening this to soft-deleted fields would pull an archived unique-key field
            // back into every document's hash - not just the recycle-bin ones this fix targets -  and
            // FlexFieldFingerprintCalculator.Compute nulls the WHOLE fingerprint on a partial key, so every
            // live document that (correctly) never held a value for the now-archived field would silently
            // lose duplicate detection. That is also the reverse of #528's rule that an archived unique-key
            // field narrows the key rather than resurrecting it.
            List<Document> page;
            using (DataFilter.Disable<ISoftDelete>())
            {
                page = await DocumentGenericRepository.GetPagedListAsync(
                    skip, BatchSize, sorting: nameof(Document.Id), includeDetails: false, cancellationToken);
            }

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
                // One flush per page, as in MigrateValuesAsync.
                await DocumentGenericRepository.UpdateAsync(document, autoSave: false, cancellationToken);
                recomputed++;
            }

            await UnitOfWorkManager.Current!.SaveChangesAsync(cancellationToken);

            skip += page.Count;
        }

        Logger.LogInformation(
            "Recomputed {Count} document fingerprints from the v3 value bag for layer {Layer}.",
            recomputed, CurrentTenant.Id?.ToString() ?? "host");

        return recomputed;
    }

    /// <summary>
    /// One page of document ids, ordered by id.
    /// <para>
    /// A projection rather than a page of entities: the callers need the id to decide whether a document
    /// is worth loading at all, and loading whole documents to find that out means paying for every
    /// <c>Markdown</c> body twice.
    /// </para>
    /// <para>
    /// Ordering by id is what makes paging safe while the loop writes: it is stable and unaffected by the
    /// updates each iteration makes, so no document is skipped or seen twice.
    /// </para>
    /// </summary>
    protected virtual async Task<List<Guid>> PageDocumentIdsAsync(int skipCount, CancellationToken cancellationToken)
    {
        var queryable = await DocumentGenericRepository.GetQueryableAsync();

        return await AsyncExecuter.ToListAsync(
            queryable.OrderBy(d => d.Id).Skip(skipCount).Take(BatchSize).Select(d => d.Id),
            cancellationToken);
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
