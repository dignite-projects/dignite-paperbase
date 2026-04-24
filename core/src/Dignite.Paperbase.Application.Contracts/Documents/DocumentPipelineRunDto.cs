using System;
using Dignite.Paperbase.Documents;

namespace Dignite.Paperbase.Documents;

public class DocumentPipelineRunDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string PipelineCode { get; set; } = default!;
    public PipelineRunStatus Status { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResultCode { get; set; }
    public string? ErrorMessage { get; set; }
}
