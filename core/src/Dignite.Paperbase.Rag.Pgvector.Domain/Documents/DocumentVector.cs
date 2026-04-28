using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

/// <summary>
/// Document-level mean-pooled embedding. <see cref="Entity{TKey}.Id"/> == DocumentId.
/// Written atomically with its source chunks inside <c>UpsertDocumentAsync</c> (same UoW).
/// Provides O(1) per-document vector lookup for relation-inference similarity search,
/// avoiding the chunk-fetch + mean-pool step at query time.
/// </summary>
public class DocumentVector : AggregateRoot<Guid>, IMultiTenant
{
    /// <summary>Tenant that owns this document. Null for host-level documents.</summary>
    public virtual Guid? TenantId { get; private set; }

    /// <summary>Document type code as registered in DocumentTypeOptions.</summary>
    public virtual string? DocumentTypeCode { get; private set; }

    /// <summary>Mean-pooled embedding of all chunks. Dimension == DocumentChunkConsts.EmbeddingVectorDimension.</summary>
    public virtual float[] EmbeddingVector { get; private set; } = default!;

    /// <summary>Number of source chunks used to compute EmbeddingVector.</summary>
    public virtual int ChunkCount { get; private set; }

    protected DocumentVector() { }

    /// <param name="documentId">Also becomes the entity Id.</param>
    public DocumentVector(
        Guid documentId,
        Guid? tenantId,
        string? documentTypeCode,
        float[] embeddingVector,
        int chunkCount)
        : base(documentId)
    {
        TenantId = tenantId;
        DocumentTypeCode = documentTypeCode;
        EmbeddingVector = ValidateVector(embeddingVector);
        ChunkCount = ValidateChunkCount(chunkCount);
    }

    public virtual void Update(
        Guid? tenantId,
        string? documentTypeCode,
        float[] embeddingVector,
        int chunkCount)
    {
        ValidateTenantUnchanged(tenantId);
        DocumentTypeCode = documentTypeCode;
        EmbeddingVector = ValidateVector(embeddingVector);
        ChunkCount = ValidateChunkCount(chunkCount);
    }

    protected virtual void ValidateTenantUnchanged(Guid? tenantId)
    {
        if (TenantId != tenantId)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkTenantImmutable)
                .WithData("Existing", TenantId?.ToString("D") ?? "<host>")
                .WithData("Incoming", tenantId?.ToString("D") ?? "<host>");
        }
    }

    protected virtual float[] ValidateVector(float[] embeddingVector)
    {
        Check.NotNull(embeddingVector, nameof(embeddingVector));
        if (embeddingVector.Length != DocumentChunkConsts.EmbeddingVectorDimension)
        {
            throw new BusinessException(PgvectorRagErrorCodes.EmbeddingDimensionMismatch)
                .WithData("Expected", DocumentChunkConsts.EmbeddingVectorDimension)
                .WithData("Actual", embeddingVector.Length);
        }
        return embeddingVector;
    }

    protected virtual int ValidateChunkCount(int chunkCount)
    {
        if (chunkCount <= 0)
        {
            throw new BusinessException(PgvectorRagErrorCodes.DocumentChunkIndexOutOfRange)
                .WithData("ChunkCount", chunkCount);
        }
        return chunkCount;
    }
}
