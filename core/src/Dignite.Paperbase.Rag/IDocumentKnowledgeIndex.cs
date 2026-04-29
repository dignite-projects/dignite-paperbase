using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Paperbase business-level facade for Qdrant-backed knowledge index operations.
/// This is not a generic vector database abstraction — it carries Paperbase-specific
/// semantics: multi-tenancy, document identity, score normalization, and source citation.
/// </summary>
public interface IDocumentKnowledgeIndex
{
    /// <summary>
    /// Insert or update all chunk records for one document (whole-document replace).
    /// Idempotent: calling with the same DocumentId replaces existing chunks.
    /// Passing an empty <see cref="DocumentVectorIndexUpdate.Chunks"/> list removes all index
    /// data for the document.
    /// </summary>
    Task UpsertDocumentAsync(
        DocumentVectorIndexUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all chunk records for the specified document.
    /// Used during document deletion when the deletion path is not already covered by
    /// <see cref="UpsertDocumentAsync"/> (e.g., cascaded from an event handler).
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
