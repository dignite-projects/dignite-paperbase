using System;
using Dignite.Paperbase.Documents;

namespace Dignite.Paperbase.Domain.Documents.Events;

/// <summary>
/// 文档生命周期状态变更的本地域事件。
/// 在 <see cref="Document.TransitionLifecycle"/> 内通过 AddLocalEvent 发布，与状态变更同事务。
/// 典型监听场景：通知用户文档处理完成/失败、触发审批流、更新统计数据等。
/// </summary>
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
