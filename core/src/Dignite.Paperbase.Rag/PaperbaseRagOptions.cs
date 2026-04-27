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
    /// Minimum acceptable score in [0, 1]. Results below this threshold are discarded.
    /// Only applied when the provider reports NormalizesScore = true.
    /// </summary>
    public double MinScore { get; set; } = 0.65;

    /// <summary>
    /// Default search mode used when a request does not specify one explicitly.
    /// </summary>
    public VectorSearchMode DefaultSearchMode { get; set; } = VectorSearchMode.Vector;
}
