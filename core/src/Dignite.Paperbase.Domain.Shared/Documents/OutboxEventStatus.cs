namespace Dignite.Paperbase.Documents;

/// <summary>
/// 出口事件去重状态。配合 <see cref="OutboxEvent"/> 实现"同一 key 未消费时替换、已消费时再发"语义。
/// </summary>
public enum OutboxEventStatus
{
    /// <summary>
    /// 事件已发出但下游尚未确认消费。新事件到达同一 (TenantId, DocumentId, EventType) key
    /// 时会替换旧事件（更新 ScheduledAt），不会重复发布。
    /// </summary>
    InFlight = 0,

    /// <summary>
    /// 事件已被下游确认消费。新事件到达同一 key 时视作 update，重新走 InFlight 流程。
    /// </summary>
    Consumed = 1
}
