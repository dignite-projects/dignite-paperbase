using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

/// <summary>
/// 文档文本分块及其向量表示。独立聚合根，由 Embedding 流水线写入、由 RAG 问答检索。
/// 向量维度由 <see cref="DocumentChunkConsts.EmbeddingVectorDimension"/> 全局统一；
/// Document 删除时由 EF 级联清理，亦可通过 <see cref="IDocumentChunkRepository.DeleteByDocumentIdAsync"/> 主动清理。
/// ChunkIndex 在文档内唯一，不应跨文档比较。
///
/// <para>
/// 反范式化字段（<see cref="DocumentTypeCode"/> / <see cref="Title"/> / <see cref="PageNumber"/>）从
/// Document 聚合冗余复制到 chunk 行上，让 provider 检索路径不再需要 JOIN Documents 表——这是为
/// Slice C 切独立 <c>PgvectorRagDbContext</c>（甚至跨 DBMS）做的物理前置。
/// </para>
///
/// <para>
/// <b>一致性约束：</b><see cref="DocumentTypeCode"/> 在 Document 分类阶段确定，目前认为之后基本不变；
/// 一旦未来允许 Document 重新分类，必须发领域事件让 chunks 同步更新此字段——否则反范式数据会陈旧。
/// 本 Slice 不引入该事件，仅在此声明约束（参见 #39）。
/// </para>
/// </summary>
public class DocumentChunk : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual Guid DocumentId { get; private set; }

    public virtual int ChunkIndex { get; private set; }

    public virtual string ChunkText { get; private set; } = default!;

    public virtual float[] EmbeddingVector { get; private set; } = default!;

    /// <summary>
    /// 反范式化的文档类型码（来自 Document 分类）。allow null 与 Document.DocumentTypeCode 语义一致：
    /// 文档尚未成功分类时该字段为 null。
    /// </summary>
    public virtual string? DocumentTypeCode { get; private set; }

    /// <summary>
    /// 反范式化的章节/页面 Title，用于 source citation。当前 embedding pipeline 暂未生成 chunk-level title，
    /// 因此实际写入仍为 null；保留字段是为了未来扩展（如 PDF outline 抽取）不再加 schema 改动。
    /// </summary>
    public virtual string? Title { get; private set; }

    /// <summary>
    /// 反范式化的 1-based 页码，用于 source citation。语义同 <see cref="Title"/>：占位字段，目前为 null。
    /// </summary>
    public virtual int? PageNumber { get; private set; }

    protected DocumentChunk() { }

    public DocumentChunk(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        int chunkIndex,
        string chunkText,
        float[] embeddingVector,
        string? documentTypeCode = null,
        string? title = null,
        int? pageNumber = null)
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
        DocumentTypeCode = documentTypeCode;
        Title = ValidateTitle(title);
        PageNumber = pageNumber;
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
        float[] embeddingVector,
        string? documentTypeCode = null,
        string? title = null,
        int? pageNumber = null)
    {
        ValidateTenantUnchanged(tenantId);
        ValidateDocumentUnchanged(documentId);
        ChunkIndex = ValidateChunkIndex(chunkIndex);
        ChunkText = Check.NotNullOrWhiteSpace(
            chunkText,
            nameof(chunkText),
            DocumentChunkConsts.MaxChunkTextLength);
        EmbeddingVector = ValidateVector(embeddingVector);
        DocumentTypeCode = documentTypeCode;
        Title = ValidateTitle(title);
        PageNumber = pageNumber;
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

    protected virtual string? ValidateTitle(string? title)
    {
        if (title is null)
            return null;
        // 防御性截断：Title 是来源 citation，不是核心索引字段——超长时优先保留 chunk 写入成功，
        // 而非让 embedding pipeline 因 schema 边界失败。EF 列宽度的同名常量做硬上限校验。
        return title.Length > DocumentChunkConsts.MaxTitleLength
            ? title[..DocumentChunkConsts.MaxTitleLength]
            : title;
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

    private static string FormatTenantId(Guid? tenantId)
    {
        return tenantId?.ToString("D") ?? "<host>";
    }
}
