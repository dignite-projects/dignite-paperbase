using System;
using Dignite.Paperbase.Documents;

namespace Dignite.Paperbase.Domain.Documents.Events;

public class DocumentPipelineRunCompletedEvent
{
    public Guid DocumentId { get; }
    public string PipelineCode { get; }
    public PipelineRunStatus Status { get; }
    public string? ResultCode { get; }

    public DocumentPipelineRunCompletedEvent(
        Guid documentId,
        string pipelineCode,
        PipelineRunStatus status,
        string? resultCode)
    {
        DocumentId = documentId;
        PipelineCode = pipelineCode;
        Status = status;
        ResultCode = resultCode;
    }
}
