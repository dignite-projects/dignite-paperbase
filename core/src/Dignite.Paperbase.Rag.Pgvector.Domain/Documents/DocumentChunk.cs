using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

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

    public virtual float[] EmbeddingVector { get; private set; } = default!;

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
        DocumentId = ValidateDocumentId(documentId);
        ChunkIndex = ValidateChunkIndex(chunkIndex);
        ChunkText = Check.NotNullOrWhiteSpace(
            chunkText,
            nameof(chunkText),
            DocumentChunkConsts.MaxChunkTextLength);
        EmbeddingVector = ValidateVector(embeddingVector);
    }

    public virtual void UpdateEmbedding(float[] embeddingVector)
    {
        EmbeddingVector = ValidateVector(embeddingVector);
    }

    public virtual void UpdateRecord(
        Guid? tenantId,
        Guid documentId,
        int chunkIndex,
        string chunkText,
        float[] embeddingVector)
    {
        ValidateTenantUnchanged(tenantId);
        ValidateDocumentUnchanged(documentId);
        ChunkIndex = ValidateChunkIndex(chunkIndex);
        ChunkText = Check.NotNullOrWhiteSpace(
            chunkText,
            nameof(chunkText),
            DocumentChunkConsts.MaxChunkTextLength);
        EmbeddingVector = ValidateVector(embeddingVector);
    }

    protected virtual Guid ValidateDocumentId(Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkDocumentIdRequired);
        }
        return documentId;
    }

    protected virtual void ValidateTenantUnchanged(Guid? tenantId)
    {
        if (TenantId != tenantId)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkTenantImmutable)
                .WithData("Existing", FormatTenantId(TenantId))
                .WithData("Incoming", FormatTenantId(tenantId));
        }
    }

    protected virtual void ValidateDocumentUnchanged(Guid documentId)
    {
        ValidateDocumentId(documentId);
        if (DocumentId != documentId)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkDocumentImmutable)
                .WithData("Existing", DocumentId)
                .WithData("Incoming", documentId);
        }
    }

    protected virtual int ValidateChunkIndex(int chunkIndex)
    {
        if (chunkIndex < 0)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkIndexOutOfRange)
                .WithData("ChunkIndex", chunkIndex);
        }
        return chunkIndex;
    }

    protected virtual float[] ValidateVector(float[] embeddingVector)
    {
        Check.NotNull(embeddingVector, nameof(embeddingVector));
        if (embeddingVector.Length != PaperbaseDbProperties.EmbeddingVectorDimension)
        {
            throw new BusinessException(PgvectorRagErrorCodes.EmbeddingDimensionMismatch)
                .WithData("Expected", PaperbaseDbProperties.EmbeddingVectorDimension)
                .WithData("Actual", embeddingVector.Length);
        }
        return embeddingVector;
    }

    private static string FormatTenantId(Guid? tenantId)
    {
        return tenantId?.ToString("D") ?? "<host>";
    }
}
