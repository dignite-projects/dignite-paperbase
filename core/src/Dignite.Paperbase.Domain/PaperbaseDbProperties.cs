namespace Dignite.Paperbase;

public static class PaperbaseDbProperties
{
    public static string DbTablePrefix { get; set; } = "Paperbase";

    public static string? DbSchema { get; set; } = null;

    public const string ConnectionStringName = "Paperbase";

    /// <summary>
    /// pgvector 列维度。OpenAI text-embedding-3-small = 1536。
    /// 切换模型时须同步修改此常量并重新生成迁移。
    /// </summary>
    public const int EmbeddingVectorDimension = 1536;
}
