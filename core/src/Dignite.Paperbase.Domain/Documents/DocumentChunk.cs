using System;
using Dignite.Paperbase.Documents;
using Pgvector;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 文档文本分块及其向量表示。独立聚合根，由 Embedding 流水线写入、由 RAG 问答检索。
/// 向量维度由 <see cref="PaperbaseDbProperties.EmbeddingVectorDimension"/> 全局统一；
/// Document 删除时由 EF 级联清理，亦可通过 <see cref="IDocumentChunkRepository.DeleteByDocumentIdAsync"/> 主动清理。
/// ChunkIndex 在文档内唯一，不应跨文档比较。
/// </summary>
public class DocumentChunk : CreationAuditedAggregateRoot<Guid>, IMultiTenant
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
        Vector embeddingVector)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = ValidateDocumentId(documentId);
        ChunkIndex = ValidateChunkIndex(chunkIndex);
        ChunkText = Check.NotNullOrWhiteSpace(
            chunkText,
            nameof(chunkText),
            DocumentChunkConsts.MaxChunkTextLength);
        EmbeddingVector = ValidateVector(embeddingVector);
    }

    public virtual void UpdateEmbedding(Vector embeddingVector)
    {
        EmbeddingVector = ValidateVector(embeddingVector);
    }

    protected virtual Guid ValidateDocumentId(Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentChunkDocumentIdRequired);
        }
        return documentId;
    }

    protected virtual int ValidateChunkIndex(int chunkIndex)
    {
        if (chunkIndex < 0)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentChunkIndexOutOfRange)
                .WithData("ChunkIndex", chunkIndex);
        }
        return chunkIndex;
    }

    protected virtual Vector ValidateVector(Vector embeddingVector)
    {
        Check.NotNull(embeddingVector, nameof(embeddingVector));
        var dimension = embeddingVector.ToArray().Length;
        if (dimension != PaperbaseDbProperties.EmbeddingVectorDimension)
        {
            throw new BusinessException(PaperbaseErrorCodes.EmbeddingDimensionMismatch)
                .WithData("Expected", PaperbaseDbProperties.EmbeddingVectorDimension)
                .WithData("Actual", dimension);
        }
        return embeddingVector;
    }
}
