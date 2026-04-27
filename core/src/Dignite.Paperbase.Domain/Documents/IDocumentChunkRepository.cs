using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface IDocumentChunkRepository : IRepository<DocumentChunk, Guid>
{
    Task<List<DocumentChunk>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按向量余弦相似度搜索最近邻 chunks。
    /// <paramref name="documentId"/> 与 <paramref name="documentTypeCode"/> 互斥：前者优先，
    /// 给定时只在该文档内检索（单文档 Q&A）；否则按 <paramref name="documentTypeCode"/>
    /// 跨文档检索（按类型过滤）；两者都为空则在当前租户全部文档上检索。
    /// 多租户过滤由 ABP 全局查询过滤器自动施加，调用方无需传入 TenantId。
    /// </summary>
    Task<List<DocumentChunk>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        Guid? documentId = null,
        string? documentTypeCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="SearchByVectorAsync"/> 的"带分数"版本：除返回 chunks 外还附带每条的 cosine 距离，
    /// 用于 Application 层的阈值过滤 / Rerank 输入。语义、过滤规则和租户隔离与不带分数版本完全一致。
    /// </summary>
    Task<List<DocumentChunkSearchResult>> SearchByVectorWithScoresAsync(
        float[] queryVector,
        int topK,
        Guid? documentId = null,
        string? documentTypeCode = null,
        CancellationToken cancellationToken = default);
}
