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
}
