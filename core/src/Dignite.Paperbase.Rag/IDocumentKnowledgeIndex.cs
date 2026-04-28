using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Paperbase business-level facade for knowledge index operations.
/// This is not a generic vector database abstraction — it carries Paperbase-specific
/// semantics: multi-tenancy, document identity, score normalization, and source citation.
/// Provider implementations should map this interface to their underlying store
/// (e.g., EF Core + pgvector, Azure AI Search, Qdrant).
/// </summary>
public interface IDocumentKnowledgeIndex
{
    /// <summary>Describes the capabilities of this provider.</summary>
    DocumentKnowledgeIndexCapabilities Capabilities { get; }

    /// <summary>
    /// Insert or update a batch of chunk records in the index.
    /// Implementations must be idempotent: upserting an existing record by Id
    /// updates mutable chunk fields while preserving tenant and document ownership.
    /// </summary>
    Task UpsertAsync(
        IReadOnlyList<DocumentVectorRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all chunk records belonging to the specified document.
    /// Used during document deletion and embedding rebuild.
    /// </summary>
    Task DeleteByDocumentIdAsync(
        Guid documentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search the index according to the supplied request parameters.
    /// Results are ordered by relevance descending.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
