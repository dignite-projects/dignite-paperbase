using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Services;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 出口事件发布的统一入口，负责<em>去重</em>状态追踪 + 通过 ABP DistributedEventBus 发出事件。
/// <para>
/// 调用方传入 <typeparamref name="TEto"/> + 路由 key（TenantId、DocumentId）；
/// Manager 按 <c>(TenantId, DocumentId, nameof(TEto))</c> 维度查 <see cref="OutboxEvent"/> 表：
/// <list type="bullet">
///   <item>无记录 → insert + publish</item>
///   <item>有记录且 Status = InFlight → 更新 ScheduledAt + publish（替换语义）</item>
///   <item>有记录且 Status = Consumed → 重置为 InFlight + publish（迭代更新语义）</item>
/// </list>
/// </para>
/// <para>
/// 安全约束：所有写入路径必须在调用方持有的事务/UoW 内，避免事件已发出但 Outbox 状态回滚。
/// 与 ABP transactional outbox（保证不丢）正交——这里追踪的是"语义层"重复，不替代 ABP outbox。
/// </para>
/// </summary>
public class OutboxEventManager : DomainService
{
    private readonly IOutboxEventRepository _outboxEventRepository;
    private readonly IDistributedEventBus _distributedEventBus;

    public OutboxEventManager(
        IOutboxEventRepository outboxEventRepository,
        IDistributedEventBus distributedEventBus)
    {
        _outboxEventRepository = outboxEventRepository;
        _distributedEventBus = distributedEventBus;
    }

    /// <summary>
    /// 按去重语义发布事件。同一 (TenantId, DocumentId, EventType) 未消费时替换；已消费时再发 update。
    /// </summary>
    public virtual async Task PublishAsync<TEto>(
        Guid? tenantId,
        Guid documentId,
        TEto eto,
        CancellationToken cancellationToken = default)
        where TEto : class
    {
        var eventType = typeof(TEto).Name;
        var now = Clock.Now;

        var existing = await _outboxEventRepository.FindByKeyAsync(
            tenantId, documentId, eventType, cancellationToken);

        if (existing == null)
        {
            var outboxEvent = new OutboxEvent(
                GuidGenerator.Create(),
                tenantId,
                documentId,
                eventType,
                now);

            await _outboxEventRepository.InsertAsync(outboxEvent, autoSave: true, cancellationToken);
        }
        else
        {
            existing.Reschedule(now);
            await _outboxEventRepository.UpdateAsync(existing, autoSave: true, cancellationToken);
        }

        await _distributedEventBus.PublishAsync(eto);
    }

    /// <summary>
    /// 下游确认消费——更新 Outbox 状态为 Consumed。调用方应在确保下游真的消费完毕时调用
    /// （例如收到 ack 回执 / 显式 API 上报）。
    /// </summary>
    public virtual async Task MarkConsumedAsync(
        Guid? tenantId,
        Guid documentId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var existing = await _outboxEventRepository.FindByKeyAsync(
            tenantId, documentId, eventType, cancellationToken);

        if (existing == null)
        {
            return;
        }

        existing.MarkConsumed(Clock.Now);
        await _outboxEventRepository.UpdateAsync(existing, autoSave: true, cancellationToken);
    }
}
