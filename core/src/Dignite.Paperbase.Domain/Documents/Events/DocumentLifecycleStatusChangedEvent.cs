using System;
using Dignite.Paperbase.Documents;

namespace Dignite.Paperbase.Domain.Documents.Events;

public class DocumentLifecycleStatusChangedEvent
{
    public Guid DocumentId { get; }
    public DocumentLifecycleStatus OldStatus { get; }
    public DocumentLifecycleStatus NewStatus { get; }

    public DocumentLifecycleStatusChangedEvent(
        Guid documentId,
        DocumentLifecycleStatus oldStatus,
        DocumentLifecycleStatus newStatus)
    {
        DocumentId = documentId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}
