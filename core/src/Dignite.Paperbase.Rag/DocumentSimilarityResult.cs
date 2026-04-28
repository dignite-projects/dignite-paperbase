using System;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// One result entry from <see cref="IDocumentKnowledgeIndex.SearchSimilarDocumentsAsync"/>.
/// Represents a document (not a chunk) ranked by similarity to the query document.
/// </summary>
public class DocumentSimilarityResult
{
    /// <summary>Id of the similar document.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Document type code of the similar document.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>Similarity score ∈ [0, 1] — higher means more similar.</summary>
    public double Score { get; init; }
}
