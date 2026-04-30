using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Documents;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

public class Document : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    // 多租户
    public virtual Guid? TenantId { get; private set; }

    /// <summary>BlobStore 中的 Key，写入后不可修改</summary>
    public virtual string OriginalFileBlobName { get; private set; } = default!;

    public virtual SourceType SourceType { get; private set; }

    /// <summary>文件来源信息（不可变）</summary>
    public virtual FileOrigin FileOrigin { get; private set; } = default!;

    /// <summary>
    /// 文档类型标识（由分类流水线 Run 成功后写入）。
    /// null 表示当前没有已确认/可用的文档类型；是否等待人工确认由 <see cref="ReviewStatus"/> 表达。
    /// </summary>
    public virtual string? DocumentTypeCode { get; private set; }

    /// <summary>
    /// 文档宏观生命周期状态。
    /// 由 DocumentPipelineRunManager 根据关键流水线的 Run 结果派生，不由应用层直接设置。
    /// </summary>
    public virtual DocumentLifecycleStatus LifecycleStatus { get; private set; }

    /// <summary>
    /// 人工审核状态。
    /// 分类置信度不足或无法产出有效类型时自动置为 PendingReview；人工确认后置为 Reviewed；
    /// 新一轮自动分类成功时重置为 None。
    /// </summary>
    public virtual DocumentReviewStatus ReviewStatus { get; private set; }

    /// <summary>提取的文本内容（文本提取流水线 Run 成功后写入，不可变）</summary>
    public virtual string? ExtractedText { get; private set; }

    /// <summary>
    /// 文档分类置信度（0.0 ~ 1.0），为最后一次成功分类 Run 的快照。
    /// 当 <see cref="DocumentTypeCode"/> 为 null 时此值为 0；是否等待人工确认由 <see cref="ReviewStatus"/> 表达。
    /// 人工确认（<see cref="DocumentReviewStatus.Reviewed"/>）时固定写入 1.0。
    /// </summary>
    public virtual double ClassificationConfidence { get; private set; }

    /// <summary>分类原因说明（低置信度时由 AI 填写；人工确认后清空）</summary>
    public virtual string? ClassificationReason { get; private set; }

    // --- 聚合内的 PipelineRun 集合 ---

    public virtual IReadOnlyCollection<DocumentPipelineRun> PipelineRuns
        => _pipelineRuns.AsReadOnly();

    private readonly List<DocumentPipelineRun> _pipelineRuns = new();

    // --- 派生访问器 ---

    /// <summary>
    /// 文档是否已完成向量化。
    /// false 时文档暂不可被向量检索命中，不影响分类和字段提取。
    /// </summary>
    public bool HasEmbedding
        => GetLatestRun(PaperbasePipelines.Embedding)?.Status == PipelineRunStatus.Succeeded;

    /// <summary>根据 PipelineCode 查询最近一次 Run（按 AttemptNumber 降序）。</summary>
    public DocumentPipelineRun? GetLatestRun(string pipelineCode)
        => _pipelineRuns
            .Where(r => r.PipelineCode == pipelineCode)
            .OrderByDescending(r => r.AttemptNumber)
            .FirstOrDefault();

    protected Document() { }

    public Document(
        Guid id,
        Guid? tenantId,
        string originalFileBlobName,
        SourceType sourceType,
        FileOrigin fileOrigin)
        : base(id)
    {
        TenantId = tenantId;
        OriginalFileBlobName = Check.NotNullOrWhiteSpace(originalFileBlobName, nameof(originalFileBlobName));
        SourceType = sourceType;
        FileOrigin = Check.NotNull(fileOrigin, nameof(fileOrigin));
        LifecycleStatus = DocumentLifecycleStatus.Uploaded;
    }

    // --- 写入方法（由 DocumentPipelineRunManager 在流水线完成后调用） ---

    internal void SetExtractedText(string extractedText)
    {
        if (!string.IsNullOrEmpty(ExtractedText))
            throw new BusinessException(PaperbaseErrorCodes.ExtractedTextIsImmutable);
        ExtractedText = extractedText;
    }

    internal void SetSourceType(SourceType sourceType)
    {
        SourceType = sourceType;
    }

    internal void ApplyAutomaticClassificationResult(
        string documentTypeCode,
        double classificationConfidence,
        string? reason = null)
    {
        DocumentTypeCode = Check.NotNullOrWhiteSpace(documentTypeCode, nameof(documentTypeCode));
        ClassificationConfidence = Check.Range(classificationConfidence, nameof(classificationConfidence), 0d, 1d);
        ClassificationReason = reason;
        ReviewStatus = DocumentReviewStatus.None;
    }

    /// <summary>
    /// 标记为待人工审核：清空尚未确认的分类结果，避免历史值污染外部读模型。
    /// </summary>
    internal void RequestClassificationReview(string? reason = null)
    {
        DocumentTypeCode = null;
        ClassificationConfidence = 0;
        ClassificationReason = reason;
        ReviewStatus = DocumentReviewStatus.PendingReview;
    }

    internal void ConfirmClassification(string documentTypeCode)
    {
        DocumentTypeCode = Check.NotNullOrWhiteSpace(documentTypeCode, nameof(documentTypeCode));
        ClassificationConfidence = 1.0;
        ReviewStatus = DocumentReviewStatus.Reviewed;
        ClassificationReason = null;
    }

    internal void TransitionLifecycle(DocumentLifecycleStatus newStatus)
    {
        if (LifecycleStatus == newStatus)
            return;

        var oldStatus = LifecycleStatus;
        LifecycleStatus = newStatus;
        AddLocalEvent(new DocumentLifecycleStatusChangedEvent(Id, oldStatus, newStatus));
    }

    // --- 内部 PipelineRun 集合管理（仅 DocumentPipelineRunManager 可访问） ---

    internal void AddPipelineRun(DocumentPipelineRun run)
    {
        _pipelineRuns.Add(run);
    }

    internal void PublishPipelineRunCompletedEvent(DocumentPipelineRunCompletedEvent evt)
    {
        AddLocalEvent(evt);
    }
}
