namespace Dignite.Paperbase.Documents;

public enum QaMode
{
    /// <summary>自动：有 embedding 用 RAG，否则降级全文</summary>
    Auto = 0,
    /// <summary>强制使用 RAG 向量检索</summary>
    Rag = 1,
    /// <summary>强制使用全文检索</summary>
    FullText = 2
}
