using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
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
        var tenantId = CurrentTenant.Id;
        Logger.LogInformation(
            "Field architecture v3 migration starting for layer {Layer}.",
            tenantId?.ToString() ?? "host");

        var definitionsMigrated = await MigrateDefinitionsAsync(cancellationToken);
        var (documentsMigrated, valuesMigrated) = await MigrateValuesAsync(cancellationToken);

        // The derived index is never hand-migrated: re-deriving it from the bags is the kernel's own
        // designed path, and running it here doubles as the first real exercise of the rebuild that every
        // later field-type or searchability change will depend on.
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
