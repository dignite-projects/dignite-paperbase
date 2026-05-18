using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 出口事件去重状态表。维护 (TenantId, DocumentId, EventType) 维度下事件的 InFlight / Consumed 状态，
/// 让 OutboxEventManager 能在同一 key 上做"未消费时替换、已消费时再发"的语义。
/// <para>
/// 与 ABP DistributedEventBus 内置 Outbox 的区别：ABP Outbox 是 transactional outbox（确保事件
/// 与领域更新原子性）；本表是<em>语义性</em>去重（避免重复消费 + 同一文档迭代更新时不污染下游）。
/// 两者可以共存——ABP Outbox 处理"事件不丢"，本表处理"事件不冗"。
/// </para>
/// </summary>
public class OutboxEvent : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual Guid DocumentId { get; private set; }

    /// <summary>
    /// 事件类型标识——通常是 ETO 类型的 <see cref="System.Reflection.MemberInfo.Name"/>，
    /// 例如 <c>"DocumentClassifiedEto"</c>。
    /// </summary>
    public virtual string EventType { get; private set; } = default!;

    public virtual OutboxEventStatus Status { get; private set; }

    /// <summary>
    /// 事件最近一次发布时间。同一 key 的 InFlight 事件被替换时会更新此字段。
    /// </summary>
    public virtual DateTime ScheduledAt { get; private set; }

    /// <summary>
    /// 事件被下游确认消费的时间。Status == Consumed 时有值。
    /// </summary>
    public virtual DateTime? ConsumedAt { get; private set; }

    protected OutboxEvent()
    {
    }

    public OutboxEvent(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        string eventType,
        DateTime scheduledAt)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        EventType = Check.NotNullOrWhiteSpace(eventType, nameof(eventType), OutboxEventConsts.MaxEventTypeLength);
        Status = OutboxEventStatus.InFlight;
        ScheduledAt = scheduledAt;
    }

    /// <summary>
    /// 同一 key 上新事件到达——刷新 ScheduledAt，状态回到 InFlight。
    /// </summary>
    internal void Reschedule(DateTime scheduledAt)
    {
        Status = OutboxEventStatus.InFlight;
        ScheduledAt = scheduledAt;
        ConsumedAt = null;
    }

    /// <summary>
    /// 下游确认消费——标记 Consumed，记录时间戳。
    /// </summary>
    internal void MarkConsumed(DateTime consumedAt)
    {
        Status = OutboxEventStatus.Consumed;
        ConsumedAt = consumedAt;
    }
}
