using System;
using Dignite.Abp.FlexFields.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// One derived, typed projection of one value of one field of one document (field architecture v3, #558):
/// the table that makes "find documents where field X matches Y" a SQL seek instead of an in-memory scan.
/// <para>
/// <b>Derived, never authoritative.</b> The real value always lives in <see cref="Document.FlexFields"/>,
/// and every row here is re-derivable from it through
/// <c>IFlexFieldIndexManager&lt;Document&gt;.RebuildAsync()</c>. That is what makes a field's type change
/// or a Searchable flag flip a rebuild rather than a data migration — and it is why the v2 -> v3 migration
/// never hand-writes these rows (#561).
/// </para>
/// <para>
/// These rows key on <b>FieldId</b> while the value bag keys on <c>Field.Name</c>. The asymmetry is
/// deliberate: renaming a field rewrites every bag, but leaves every row here untouched.
/// </para>
/// <para>
/// Relational-only by design, which is why it lives in this project rather than Domain — a pivot row is
/// what a relational store needs, and the kernel's MongoDB provider has no equivalent because it indexes
/// the bag in place. Nothing in the domain layer knows this type exists.
/// </para>
/// </summary>
public class DocumentFlexFieldIndex : FlexFieldIndexBase<Document>, IMultiTenant
{
    protected DocumentFlexFieldIndex()
    {
    }

    public DocumentFlexFieldIndex(Guid id, Guid documentId, Guid fieldId, FlexFieldIndexValue value, Guid? tenantId)
        : base(id)
    {
        DocumentId = documentId;
        TenantId = tenantId;
        SetValue(fieldId, value);
    }

    /// <summary>
    /// Vault Extract's own foreign key — the kernel maps no relationships, so the host declares this and
    /// its indexes itself.
    /// </summary>
    public virtual Guid DocumentId { get; set; }

    /// <summary>
    /// Carried so ABP's tenant filter applies to this table directly.
    /// <para>
    /// Not strictly required for correctness: the query executor composes its subquery against an
    /// already tenant-filtered document query, so a foreign row could never survive the outer filter. It
    /// is here so a query written against this table <i>without</i> going through that path is still
    /// confined to one tenant, rather than relying on every future caller remembering to join — the same
    /// defense-in-depth the v2 <c>DocumentExtractedField</c> rows already had.
    /// </para>
    /// </summary>
    public virtual Guid? TenantId { get; set; }
}
