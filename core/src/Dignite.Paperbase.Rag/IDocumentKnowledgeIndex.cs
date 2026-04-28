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
    /// Insert or update all chunk records for one document atomically (whole-document replace).
    /// Idempotent: calling with the same DocumentId replaces existing chunks and recalculates
    /// the document-level vector in the same Unit of Work.
    /// Passing an empty <see cref="DocumentVectorIndexUpdate.Chunks"/> list removes all index
    /// data (chunks + document vector) for the document.
    /// </summary>
    Task UpsertDocumentAsync(
        DocumentVectorIndexUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all chunk records and the document-level vector for the specified document.
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

    /// <summary>
    /// Find documents similar to the given document using document-level mean-pooled embeddings.
    /// Returns results ordered by similarity descending, excluding the source document itself.
    /// Only available when <see cref="DocumentKnowledgeIndexCapabilities.SupportsSearchSimilarDocuments"/> is true.
    /// </summary>
    Task<IReadOnlyList<DocumentSimilarityResult>> SearchSimilarDocumentsAsync(
        Guid documentId,
        Guid? tenantId,
        int topK,
        CancellationToken cancellationToken = default);
}
