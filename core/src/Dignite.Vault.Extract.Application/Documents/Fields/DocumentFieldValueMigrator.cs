using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Rewrites value-bag keys after a field definition is renamed, over <b>one document type's</b> documents.
/// <para>
/// <b>Why Vault Extract does not use <c>IFlexFieldValueMigrator&lt;Document&gt;</c> for this.</b> The kernel's
/// migrator renames by name alone across every host entity, which is correct under the kernel's model: a field
/// name identifies one field per host <i>type</i>. Vault Extract's fields are unique per
/// <c>(TenantId, DocumentTypeId, Name)</c> instead, so «contract» and «receipt» may each legitimately define
/// <c>invoice_no</c>. Renaming the contract one through the kernel's walk would also move every receipt's
/// <c>invoice_no</c> value to the new key — where no receipt field definition backs it, so
/// <c>AssembleExtractedFields</c> skips it and the value vanishes from the detail page, exports and the
/// <c>ExtractedFields</c> egress, while its index rows (keyed on field id, which a rename does not touch) keep
/// matching filters. A value that is filterable but unreadable, with no record of the key it used to live under.
/// </para>
/// <para>
/// The kernel anticipates exactly this — <c>FlexFieldValueMigrator&lt;TEntity&gt;</c>'s own summary says a host
/// needing different behaviour should subclass or replace it, and <c>FlexFieldEntityPager</c>'s says the kernel
/// visits every entity because it "has no way to push a bag-key predicate down through the provider's own
/// query". Vault Extract can push one: <c>DocumentTypeId</c> is an indexed column here. So this is not a
/// workaround for a kernel defect, it is the downstream half the kernel deliberately left open.
/// </para>
/// <para>
/// Scoping also fixes two things the unscoped walk got wrong beyond correctness: it no longer loads every
/// document in the tenant (each carrying its full <c>Markdown</c>) to rename one type's field, and it no longer
/// skips recycle-bin documents — <see cref="IDocumentRepository.GetIdsByDocumentTypeAsync"/> traverses soft
/// delete, so restoring a document later brings its values back under the current name.
/// </para>
/// <para>
/// Ordering is the kernel's and is not interchangeable: rule out the collision, change the definition's
/// <c>Name</c>, call this, and only then let anything synchronize the index. A document synchronized between
/// steps two and three projects nothing for the field and loses the index rows it had.
/// </para>
/// </summary>
public class DocumentFieldValueMigrator : ITransientDependency
{
    protected IDocumentRepository DocumentRepository { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected IDataFilter DataFilter { get; }

    /// <summary>
    /// Documents per flush. Deliberately smaller than the kernel's 100: a page is materialized as full
    /// entities so the bag can be mutated and saved, and a Vault Extract document carries its whole Markdown.
    /// </summary>
    protected virtual int MigrationPageSize => 50;

    public DocumentFieldValueMigrator(
        IDocumentRepository documentRepository,
        IUnitOfWorkManager unitOfWorkManager,
        IDataFilter dataFilter)
    {
        DocumentRepository = documentRepository;
        UnitOfWorkManager = unitOfWorkManager;
        DataFilter = dataFilter;
    }

    /// <summary>
    /// Moves every value stored under <paramref name="oldName"/> to <paramref name="newName"/>, for documents
    /// bound to <paramref name="documentTypeId"/> only. Returns how many documents actually changed.
    /// <para>
    /// Documents not holding the old key are left alone, so this is idempotent and safe to re-run — which it
    /// needs to be, because it flushes per page rather than atomically: a failure part way through leaves the
    /// earlier pages migrated, and re-running after fixing the cause converges.
    /// </para>
    /// </summary>
    public virtual async Task<int> RenameFieldAsync(
        Guid documentTypeId,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return 0;
        }

        var changedCount = 0;
        Guid? afterId = null;

        while (true)
        {
            // Ids first, then the documents themselves: the cursor scan must not drag Markdown back for every
            // document of this type just to find the few holding the key.
            var ids = await DocumentRepository.GetIdsByDocumentTypeAsync(
                documentTypeId, afterId, MigrationPageSize, cancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            var pageChangedCount = 0;

            // GetIdsByDocumentTypeAsync traverses soft delete, so a recycle-bin document's id can appear here
            // while a filtered load could not see it. Renaming its key is the point (a restore must not
            // resurrect the old one), so the tracked load traverses soft delete too.
            using (DataFilter.Disable<ISoftDelete>())
            {
                foreach (var id in ids)
                {
                    // includeDetails: false - the bag is a column on the aggregate root itself, and the v2
                    // child collections this would otherwise eager-load are dead weight on every page.
                    var document = await DocumentRepository.FindAsync(id, includeDetails: false, cancellationToken);
                    if (document == null || !document.RenameField(oldName, newName))
                    {
                        continue;
                    }

                    await DocumentRepository.UpdateAsync(document, autoSave: false, cancellationToken);
                    pageChangedCount++;
                }
            }

            if (pageChangedCount > 0)
            {
                // Plain statement rather than the kernel's `await (Current?.Save() ?? CompletedTask)`:
                // ConfigureAwait.Fody weaves every await in this project (unconditionally, not Release-only
                // as in the kernel's own build), and mis-weaves that conditional-access-plus-coalesce shape
                // into IL the runtime rejects with InvalidProgramException.
                var unitOfWork = UnitOfWorkManager.Current;
                if (unitOfWork != null)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }

                changedCount += pageChangedCount;
            }

            if (ids.Count < MigrationPageSize)
            {
                break;
            }

            afterId = ids[^1];
        }

        return changedCount;
    }
}
