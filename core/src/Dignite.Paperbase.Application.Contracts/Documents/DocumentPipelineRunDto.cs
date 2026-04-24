using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.ObjectExtending;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 各 pipeline 专属输出通过 <see cref="ExtensibleObject.ExtraProperties"/> 暴露（如分类候选 top-K）。
/// 约定 key 见 <c>PipelineRunExtraPropertyNames</c>。
/// </summary>
public class DocumentPipelineRunDto : ExtensibleObject
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string PipelineCode { get; set; } = default!;
    public PipelineRunStatus Status { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? StatusMessage { get; set; }
}
