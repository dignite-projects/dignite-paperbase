namespace Dignite.Paperbase.Rag;

public class DocumentKnowledgeIndexCapabilities
{
    public bool SupportsVectorSearch { get; init; }
    public bool SupportsKeywordSearch { get; init; }
    public bool SupportsHybridSearch { get; init; }

    /// <summary>
    /// Whether the provider supports tenant/document/type filters at query time.
    /// Providers that cannot filter must set this to false; Application layer will
    /// refuse to proceed to avoid cross-tenant data leakage.
    /// </summary>
    public bool SupportsStructuredFilter { get; init; }

    public bool SupportsDeleteByDocumentId { get; init; }

    /// <summary>
    /// Whether the provider guarantees Score ∈ [0, 1] with higher = more relevant.
    /// When false, Application layer must not apply MinScore directly.
    /// </summary>
    public bool NormalizesScore { get; init; }
}
