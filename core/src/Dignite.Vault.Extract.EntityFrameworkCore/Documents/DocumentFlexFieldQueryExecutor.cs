using Dignite.Abp.FlexFields.EntityFrameworkCore;
using Dignite.Vault.Extract.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Pushes a field-value filter down into SQL (#558): each condition becomes a subquery over
/// <see cref="DocumentFlexFieldIndex"/>, compared against the typed column its value type names, and
/// composed onto the document query so the whole thing resolves in one round trip.
/// <para>
/// This is the v3 counterpart of v2's <c>GetFieldMatchedIdsAsync</c>, which compiled each field query
/// into an EXISTS over the document's own child rows. Same guarantee - always pushed down, never a filter
/// over already-materialized entities - reached through the kernel's index instead of a hand-written
/// predicate per <c>FieldDataType</c>.
/// </para>
/// <para>
/// Resolves as <c>IFlexFieldQueryExecutor&lt;Document&gt;</c> by ABP's naming convention.
/// </para>
/// </summary>
public class DocumentFlexFieldQueryExecutor
    : EfCoreFlexFieldQueryExecutorBase<VaultExtractDbContext, Document, DocumentFlexFieldIndex>,
      ITransientDependency
{
    protected override string EntityIdPropertyName => nameof(DocumentFlexFieldIndex.DocumentId);

    public DocumentFlexFieldQueryExecutor(IDbContextProvider<VaultExtractDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }
}
