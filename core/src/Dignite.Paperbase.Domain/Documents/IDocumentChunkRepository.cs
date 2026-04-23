using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Domain.Documents;

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
    /// documentId：限定到单文档（Q&A）；documentTypeCode：跨文档但按类型过滤。
    /// </summary>
    Task<List<DocumentChunk>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        Guid? documentId = null,
        string? documentTypeCode = null,
        CancellationToken cancellationToken = default);
}
