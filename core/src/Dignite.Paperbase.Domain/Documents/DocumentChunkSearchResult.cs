namespace Dignite.Paperbase.Documents;

/// <summary>
/// 向量检索结果项，承载命中的 <see cref="DocumentChunk"/> 与对应的 cosine 距离/相似度。
/// 用于让 Application 层可对结果做阈值过滤、Rerank 或调试日志，而无需在 SQL 层重复发起查询。
/// </summary>
public class DocumentChunkSearchResult
{
    public DocumentChunk Chunk { get; }

    /// <summary>
    /// pgvector 计算的 cosine 距离，取值范围 [0, 2]，越小越相似。
    /// </summary>
    public double CosineDistance { get; }

    /// <summary>
    /// cosine 相似度，等价于 <c>1 - CosineDistance</c>，取值范围 [-1, 1]，越大越相似。
    /// 用作阈值过滤时比 <see cref="CosineDistance"/> 更直观。
    /// </summary>
    public double Similarity => 1.0 - CosineDistance;

    public DocumentChunkSearchResult(DocumentChunk chunk, double cosineDistance)
    {
        Chunk = chunk;
        CosineDistance = cosineDistance;
    }
}
