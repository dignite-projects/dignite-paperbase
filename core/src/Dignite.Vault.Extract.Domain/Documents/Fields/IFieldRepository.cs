using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Repository for the v3 <see cref="Field"/> aggregate. Mirrors <see cref="IFieldDefinitionRepository"/>
/// so the two can run side by side until #561's migration retires the v2 entity.
/// <para>
/// Deliberately Vault Extract's own rather than the kernel's <c>IFlexFieldRepository&lt;TField&gt;</c>:
/// that one walks the field-definition axis by <i>name</i>, which suits a downstream whose fields form a
/// tenant-wide library, but every query here is scoped by <c>DocumentTypeId</c> because a Vault Extract
/// field belongs to exactly one document type (#558 non-goal). The kernel never calls either.
/// </para>
/// </summary>
public interface IFieldRepository : IRepository<Field, Guid>
{
    /// <summary>
    /// Fields of one document type in the current ambient tenant layer, ordered by
    /// <c>DisplayOrder</c>.
    /// <para>
    /// Isolation comes from the ambient <c>IMultiTenant</c> filter, with no cross-layer read. Background
    /// paths (field extraction, index rebuild) must call <c>ICurrentTenant.Change(...)</c> so the ambient
    /// layer matches <c>Document.TenantId</c> before calling this — the same contract
    /// <see cref="IFieldDefinitionRepository"/> documents.
    /// </para>
    /// </summary>
    Task<List<Field>> GetListAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default);

    Task<Field?> FindByNameAsync(
        Guid documentTypeId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-loads fields by id — the direction a query index row, which keys on the field id rather
    /// than the name, needs in order to resolve back to its definition.
    /// </summary>
    Task<List<Field>> GetListByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether another field of the same document type already uses <paramref name="name"/>. Pass
    /// <paramref name="excludedId"/> when checking a rename.
    /// <para>
    /// Layer-scoped uniqueness on <c>(TenantId, DocumentTypeId, Name)</c> is an application-layer check
    /// rather than a DB index, for the cross-database reasons the EF mapping records — so this is the
    /// check, not a second line of defence behind one.
    /// </para>
    /// </summary>
    Task<bool> NameExistsAsync(
        Guid documentTypeId,
        string name,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default);
}
