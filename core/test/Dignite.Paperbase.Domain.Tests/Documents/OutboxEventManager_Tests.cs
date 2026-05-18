using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseDomainTestModule))]
public class OutboxEventManagerTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IOutboxEventRepository>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
    }
}

/// <summary>
/// OutboxEventManager 去重语义守护——NSubstitute mock 模式：
/// <list type="bullet">
///   <item>无记录时 publish → InsertAsync 被调用 + EventBus.PublishAsync 被调用</item>
///   <item>InFlight 记录存在 → UpdateAsync 被调用（Reschedule 改 ScheduledAt）+ EventBus.PublishAsync 被调用</item>
///   <item>MarkConsumed 后再 publish → UpdateAsync 被调用 + Reschedule 把 Status 改回 InFlight</item>
/// </list>
/// </summary>
public class OutboxEventManager_Tests : PaperbaseDomainTestBase<OutboxEventManagerTestModule>
{
    private readonly OutboxEventManager _manager;
    private readonly IOutboxEventRepository _repository;
    private readonly IDistributedEventBus _distributedEventBus;

    public OutboxEventManager_Tests()
    {
        _manager = GetRequiredService<OutboxEventManager>();
        _repository = GetRequiredService<IOutboxEventRepository>();
        _distributedEventBus = GetRequiredService<IDistributedEventBus>();
    }

    [Fact]
    public async Task First_Publish_Inserts_OutboxEvent_And_Emits_Event()
    {
        var documentId = Guid.NewGuid();
        // 默认无记录
        _repository.FindByKeyAsync(null, documentId, nameof(DocumentUploadedEto), Arg.Any<CancellationToken>())
            .Returns((OutboxEvent?)null);

        await _manager.PublishAsync(null, documentId, new DocumentUploadedEto
        {
            DocumentId = documentId,
            FileName = "x.pdf"
        });

        // Insert 被调用，且事件类型正确
        await _repository.Received(1).InsertAsync(
            Arg.Is<OutboxEvent>(e =>
                e.DocumentId == documentId &&
                e.EventType == nameof(DocumentUploadedEto) &&
                e.Status == OutboxEventStatus.InFlight),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // EventBus 被调用
        await _distributedEventBus.Received(1).PublishAsync(
            Arg.Is<DocumentUploadedEto>(e => e.DocumentId == documentId),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task Republish_Same_Key_Updates_Existing_Row_Without_Insert()
    {
        var documentId = Guid.NewGuid();
        var existing = new OutboxEvent(
            id: Guid.NewGuid(),
            tenantId: null,
            documentId: documentId,
            eventType: nameof(DocumentUploadedEto),
            scheduledAt: DateTime.UtcNow.AddMinutes(-5));

        _repository.FindByKeyAsync(null, documentId, nameof(DocumentUploadedEto), Arg.Any<CancellationToken>())
            .Returns(existing);

        await _manager.PublishAsync(null, documentId, new DocumentUploadedEto { DocumentId = documentId });

        // 不应再次 Insert——已有 InFlight 记录
        await _repository.DidNotReceive().InsertAsync(
            Arg.Any<OutboxEvent>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        // Update 被调用——ScheduledAt 刷新
        await _repository.Received(1).UpdateAsync(
            Arg.Is<OutboxEvent>(e => e.Id == existing.Id && e.Status == OutboxEventStatus.InFlight),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // EventBus 被调用
        await _distributedEventBus.Received(1).PublishAsync(
            Arg.Any<DocumentUploadedEto>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task MarkConsumed_Updates_Status_And_ConsumedAt()
    {
        var documentId = Guid.NewGuid();
        var existing = new OutboxEvent(
            id: Guid.NewGuid(),
            tenantId: null,
            documentId: documentId,
            eventType: nameof(DocumentUploadedEto),
            scheduledAt: DateTime.UtcNow);

        _repository.FindByKeyAsync(null, documentId, nameof(DocumentUploadedEto), Arg.Any<CancellationToken>())
            .Returns(existing);

        await _manager.MarkConsumedAsync(null, documentId, nameof(DocumentUploadedEto));

        await _repository.Received(1).UpdateAsync(
            Arg.Is<OutboxEvent>(e =>
                e.Id == existing.Id &&
                e.Status == OutboxEventStatus.Consumed &&
                e.ConsumedAt.HasValue),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkConsumed_NoOp_When_Record_Missing()
    {
        var documentId = Guid.NewGuid();
        _repository.FindByKeyAsync(null, documentId, nameof(DocumentUploadedEto), Arg.Any<CancellationToken>())
            .Returns((OutboxEvent?)null);

        await _manager.MarkConsumedAsync(null, documentId, nameof(DocumentUploadedEto));

        // 没有 update 被调用
        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<OutboxEvent>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
