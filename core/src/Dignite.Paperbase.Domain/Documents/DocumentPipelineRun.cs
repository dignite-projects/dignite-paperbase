using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// 文档流水线执行记录。
/// 一条 Document + PipelineCode + AttemptNumber 唯一确定一次执行。
/// 同一流水线可重试，每次重试产生一条新记录，AttemptNumber 自增。
/// </summary>
public class DocumentPipelineRun : Entity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    /// <summary>所属文档 ID</summary>
    public virtual Guid DocumentId { get; private set; }

    /// <summary>
    /// 流水线标识。核心常量见 <see cref="PaperbasePipelines"/>；
    /// 业务模块可注册自定义值，建议前缀 "{moduleCode}."。
    /// </summary>
    public virtual string PipelineCode { get; private set; } = default!;

    public virtual PipelineRunStatus Status { get; private set; }

    /// <summary>第几次尝试（从 1 开始，重试递增）</summary>
    public virtual int AttemptNumber { get; private set; }

    public virtual DateTime StartedAt { get; private set; }
    public virtual DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// 状态描述。失败时为异常信息、跳过时为跳过原因；成功时通常为 null。
    /// 仅用于诊断/审计展示，不承载业务语义（业务结果由相关聚合根字段表达）。
    /// </summary>
    public virtual string? StatusMessage { get; private set; }

    protected DocumentPipelineRun() { }

    internal DocumentPipelineRun(
        Guid id,
        Guid documentId,
        Guid? tenantId,
        string pipelineCode,
        int attemptNumber)
        : base(id)
    {
        DocumentId = documentId;
        TenantId = tenantId;
        PipelineCode = pipelineCode;
        AttemptNumber = attemptNumber;
        Status = PipelineRunStatus.Pending;
        StartedAt = DateTime.UtcNow;
    }

    internal void MarkRunning(DateTime now)
    {
        Status = PipelineRunStatus.Running;
        StartedAt = now;
    }

    internal void MarkSucceeded(DateTime now)
    {
        Status = PipelineRunStatus.Succeeded;
        CompletedAt = now;
    }

    internal void MarkFailed(DateTime now, string statusMessage)
    {
        Status = PipelineRunStatus.Failed;
        StatusMessage = statusMessage;
        CompletedAt = now;
    }

    internal void MarkSkipped(DateTime now, string statusMessage)
    {
        Status = PipelineRunStatus.Skipped;
        StatusMessage = statusMessage;
        CompletedAt = now;
    }
}
