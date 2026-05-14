using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface IDocumentRelationRepository : IRepository<DocumentRelation, Guid>
{
    Task<List<DocumentRelation>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<List<DocumentRelation>> GetListByDocumentIdsAsync(
        IReadOnlyCollection<Guid> documentIds,
        bool includeAiSuggested = true,
        CancellationToken cancellationToken = default);

    Task HardDeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the IDs of all documents linked to <paramref name="documentId"/> (peers from either direction).
    /// Used by L2/L3 RelationDiscovery to de-duplicate suggestions against existing relations.
    ///
    /// <para>
    /// <strong>R2 dismissal tombstone</strong>: when <paramref name="includeDismissed"/> is true, the
    /// soft-delete filter is bypassed so that user-dismissed relations (rows with IsDeleted=true)
    /// still count as "already linked" and L2/L3 won't re-suggest the same pair. Without this,
    /// the user's UI experience is "clear the AI-suggestion bin → it fills back up next run".
    /// Tenant filter is bypassed too (EF Core all-or-nothing); caller must pass
    /// <paramref name="tenantId"/> explicitly when <paramref name="includeDismissed"/> is true.
    /// </para>
    /// </summary>
    Task<List<Guid>> GetLinkedPeerDocumentIdsAsync(
        Guid documentId,
        Guid? tenantId,
        bool includeDismissed = false,
        CancellationToken cancellationToken = default);
}
