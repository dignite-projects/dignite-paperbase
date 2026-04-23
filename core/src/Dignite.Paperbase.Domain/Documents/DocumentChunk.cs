using System;
using Pgvector;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// 文档文本分块及其向量表示。独立实体，不内嵌于 Document。
/// </summary>
public class DocumentChunk : Entity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }
    public virtual Guid DocumentId { get; private set; }
    public virtual int ChunkIndex { get; private set; }
    public virtual string ChunkText { get; private set; } = default!;
    public virtual Vector EmbeddingVector { get; private set; } = default!;

    protected DocumentChunk() { }

    public DocumentChunk(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        int chunkIndex,
        string chunkText,
        float[] embeddingVector)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        ChunkIndex = chunkIndex;
        ChunkText = chunkText;
        EmbeddingVector = new Vector(embeddingVector);
    }
}
