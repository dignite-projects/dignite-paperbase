using System;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// A single result item returned from a vector / keyword / hybrid search.
/// All fields are strongly typed — no metadata dictionary.
/// Score is normalized to [0, 1] (higher = more relevant) when the provider
/// reports NormalizesScore = true.
/// </summary>
public class VectorSearchResult
{
    /// <summary>Identifier of the matched chunk record in the vector store.</summary>
    public Guid RecordId { get; init; }

    /// <summary>Source document identifier.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Document type code of the source document.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>Zero-based chunk index within the source document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Chunk text content for use as RAG context.</summary>
    public string Text { get; init; } = default!;

    /// <summary>
    /// Relevance score. Normalized to [0, 1] when the provider reports
    /// NormalizesScore = true; raw provider score otherwise.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>Optional section/page title for source citation.</summary>
    public string? Title { get; init; }

    /// <summary>Optional 1-based page number for source citation.</summary>
    public int? PageNumber { get; init; }
}
