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
    /// 流水线私有的结果码。Succeeded 时典型值："Ok"、"LowConfidence"。
    /// Failed 时典型值："Timeout"、"ProviderError"。
    /// </summary>
    public virtual string? ResultCode { get; private set; }

    /// <summary>失败时的错误信息（ResultCode 之外的可读描述）</summary>
    public virtual string? ErrorMessage { get; private set; }

    /// <summary>
    /// 流水线私有元数据（JSON）。由各流水线自行定义 schema。
    /// AI 类流水线 Metadata 由 Application 的 Workflow 在执行结果中写入。
    /// </summary>
    public virtual string? Metadata { get; private set; }

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

    internal void MarkSucceeded(DateTime now, string resultCode = "Ok", string? metadata = null)
    {
        Status = PipelineRunStatus.Succeeded;
        ResultCode = resultCode;
        Metadata = metadata;
        CompletedAt = now;
    }

    internal void MarkFailed(DateTime now, string errorMessage, string resultCode = "Error", string? metadata = null)
    {
        Status = PipelineRunStatus.Failed;
        ResultCode = resultCode;
        ErrorMessage = errorMessage;
        Metadata = metadata;
        CompletedAt = now;
    }

    internal void MarkSkipped(DateTime now, string reason, string resultCode = "Skipped")
    {
        Status = PipelineRunStatus.Skipped;
        ResultCode = resultCode;
        ErrorMessage = reason;
        CompletedAt = now;
    }
}
