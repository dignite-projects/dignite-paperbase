namespace Dignite.Paperbase.Documents;

public static class DocumentChunkConsts
{
    /// <summary>
    /// 单个 chunk 文本最大长度。按主流 embedding 模型 token 上限保守取值。
    /// </summary>
    public const int MaxChunkTextLength = 8000;
}
