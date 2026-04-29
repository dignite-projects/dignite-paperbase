using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Input for <see cref="IDocumentKnowledgeIndex.UpsertDocumentAsync"/>.
/// Represents the complete index state for one document: calling with the same
/// DocumentId a second time replaces the previous state.
/// Passing an empty <see cref="Chunks"/> list removes all index data for the document.
/// </summary>
public class DocumentVectorIndexUpdate
{
    public Guid DocumentId { get; init; }

    /// <summary>Tenant that owns this document. Null for host-level documents.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Document type code as registered in DocumentTypeOptions.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>
    /// All chunk records that constitute the document's index.
    /// An empty list signals "remove all index data for this document".
    /// </summary>
    public IReadOnlyList<DocumentVectorRecord> Chunks { get; init; } = [];
}
