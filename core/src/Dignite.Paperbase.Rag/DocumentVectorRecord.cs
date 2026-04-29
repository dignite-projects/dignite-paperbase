using System;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// A single chunk record written to (or read from) the vector index.
/// All fields are strongly typed — no metadata dictionary.
/// </summary>
public class DocumentVectorRecord
{
    /// <summary>Zero-based index of this chunk within the source document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Chunk text content used for embedding and citation.</summary>
    public string Text { get; init; } = default!;

    /// <summary>Embedding vector for this chunk.</summary>
    public ReadOnlyMemory<float> Vector { get; init; }

    /// <summary>Optional 1-based page number for source citation.</summary>
    public int? PageNumber { get; init; }
}
