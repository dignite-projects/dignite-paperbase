namespace Dignite.Paperbase.Rag.Pgvector.Documents;

public static class DocumentChunkConsts
{
    /// <summary>
    /// 单个 chunk 文本最大长度。按主流 embedding 模型 token 上限保守取值。
    /// </summary>
    public static int MaxChunkTextLength { get; set; } = 8000;

    /// <summary>
    /// 反范式化的章节/页面 Title 最大长度，用于 source citation 展示。
    /// </summary>
    public static int MaxTitleLength { get; set; } = 256;

    /// <summary>
    /// pgvector 列的向量维度，是 schema 的唯一权威来源（<c>vector(N)</c> 列类型）。
    /// 切换 embedding 模型时必须同步修改此常量并新增 <c>PgvectorRagDbContext</c> Migration。
    /// 运行时维度配置 <see cref="Dignite.Paperbase.Rag.PaperbaseRagOptions.EmbeddingDimension"/>
    /// 与本值不一致时会在启动期校验失败，明确提示需要 Migration。
    /// </summary>
    public const int EmbeddingVectorDimension = 1536;
}
