namespace Dignite.Paperbase;

public static class PaperbaseDbProperties
{
    public static string DbTablePrefix { get; set; } = "Paperbase";

    public static string? DbSchema { get; set; } = null;

    public const string ConnectionStringName = "Paperbase";

    /// <summary>
    /// Default embedding vector dimension. OpenAI text-embedding-3-small = 1536.
    /// Keep this aligned with PaperbaseRag:EmbeddingDimension and QdrantRag:VectorDimension.
    /// </summary>
    public const int EmbeddingVectorDimension = 1536;
}
