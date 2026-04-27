namespace Dignite.Paperbase.Rag.Pgvector;

/// <summary>
/// pgvector provider-specific options.
/// General RAG options (EmbeddingDimension, DefaultTopK, MinScore) live in <see cref="PaperbaseRagOptions"/>.
/// </summary>
public class PgvectorRagOptions
{
    // Reserved for future pgvector tuning parameters:
    // ef_search (HNSW), probes (IVFFlat), distance metric override, etc.
}
