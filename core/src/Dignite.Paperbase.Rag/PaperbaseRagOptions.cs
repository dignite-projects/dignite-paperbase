namespace Dignite.Paperbase.Rag;

public class PaperbaseRagOptions
{
    /// <summary>
    /// Embedding vector dimension. Must match the dimension used when building the index.
    /// Changing this value requires an index rebuild and a schema migration.
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1536;

    /// <summary>
    /// Default number of top results to retrieve per search request.
    /// </summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>
    /// Minimum acceptable normalized score in [0, 1]. Set to null to disable threshold filtering.
    /// Providers that cannot supply a normalized score (e.g. Qdrant hybrid/RRF) emit
    /// <see cref="VectorSearchResult.Score"/> = null and bypass this threshold.
    /// </summary>
    public double? MinScore { get; set; } = 0.65;
}
