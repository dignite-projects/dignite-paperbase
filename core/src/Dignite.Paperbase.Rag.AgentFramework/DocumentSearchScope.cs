using System;
using Dignite.Paperbase.Rag;

namespace Dignite.Paperbase.Rag.AgentFramework;

/// <summary>
/// Optional per-call overrides for <see cref="DocumentTextSearchAdapter"/>. Anything
/// left null falls back to <see cref="PaperbaseRagOptions"/> defaults so callers can
/// scope an Agent Framework search to a single document, document type, or change
/// the retrieval mode without rebuilding the adapter wiring.
/// </summary>
public sealed class DocumentSearchScope
{
    /// <summary>Restrict search to a single document. Null means all documents.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>Restrict search to a document type. Ignored when <see cref="DocumentId"/> is set.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>Override <see cref="PaperbaseRagOptions.DefaultTopK"/>.</summary>
    public int? TopK { get; init; }

    /// <summary>Override <see cref="PaperbaseRagOptions.MinScore"/>.</summary>
    public double? MinScore { get; init; }

    /// <summary>Override <see cref="PaperbaseRagOptions.DefaultSearchMode"/>.</summary>
    public VectorSearchMode? Mode { get; init; }
}
