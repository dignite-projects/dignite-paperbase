using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Events;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Dignite.Paperbase.Rag.Pgvector.EventHandlers;
using NSubstitute;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// <see cref="DocumentDeletingEventHandler"/> 单元测试。
/// 验证 after-commit 语义的关键约束：
/// <list type="bullet">
///   <item><description>
///     handler 在主 UoW 仍活跃时只做一件事：向 <c>IUnitOfWork.OnCompleted</c> 注册回调，
///     不直接执行任何数据库操作。
///   </description></item>
///   <item><description>
///     回调内必须以 <c>requiresNew:true</c> 开启独立 UoW，不依赖已完成的 ambient UoW。
///   </description></item>
///   <item><description>
///     回调内调用 <see cref="IDocumentChunkRepository.DeleteByDocumentIdAsync"/>，
///     并在对应 tenant 上下文内执行。
///   </description></item>
/// </list>
/// </summary>
public class DocumentDeletingEventHandler_Tests
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IUnitOfWork _ambientUow;
    private readonly IUnitOfWork _innerUow;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly DocumentDeletingEventHandler _handler;

    public DocumentDeletingEventHandler_Tests()
    {
        _ambientUow = Substitute.For<IUnitOfWork>();
        _innerUow = Substitute.For<IUnitOfWork>();
        _chunkRepository = Substitute.For<IDocumentChunkRepository>();
        _currentTenant = Substitute.For<ICurrentTenant>();

        _uowManager = Substitute.For<IUnitOfWorkManager>();
        _uowManager.Current.Returns(_ambientUow);
        _uowManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), Arg.Any<bool>())
            .Returns(_innerUow);

        // ICurrentTenant.Change() は IDisposable を返す
        _currentTenant
            .Change(Arg.Any<Guid?>())
            .Returns(Substitute.For<IDisposable>());

        _handler = new DocumentDeletingEventHandler(
            _uowManager, _chunkRepository, _currentTenant);
    }

    [Fact]
    public async Task HandleEventAsync_Registers_OnCompleted_Callback()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.HandleEventAsync(evt);

        _ambientUow.Received(1).OnCompleted(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task HandleEventAsync_Does_Not_Delete_Chunks_Immediately()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.HandleEventAsync(evt);

        // chunk 删除不能在 handler 返回时同步发生，必须推迟到 OnCompleted 回调
        await _chunkRepository.DidNotReceive()
            .DeleteByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCompleted_Callback_Begins_RequiresNew_TransactionalUoW()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), Guid.NewGuid());

        Func<Task>? capturedCallback = null;
        _ambientUow
            .When(u => u.OnCompleted(Arg.Any<Func<Task>>()))
            .Do(ci => capturedCallback = ci.Arg<Func<Task>>());

        await _handler.HandleEventAsync(evt);

        capturedCallback.ShouldNotBeNull();
        await capturedCallback!();

        _uowManager.Received(1).Begin(
            Arg.Is<AbpUnitOfWorkOptions>(o => o.IsTransactional == true),
            requiresNew: true);
    }

    [Fact]
    public async Task OnCompleted_Callback_Deletes_Chunks_For_Document()
    {
        var documentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var evt = new DocumentDeletingEvent(documentId, tenantId);

        Func<Task>? capturedCallback = null;
        _ambientUow
            .When(u => u.OnCompleted(Arg.Any<Func<Task>>()))
            .Do(ci => capturedCallback = ci.Arg<Func<Task>>());

        await _handler.HandleEventAsync(evt);
        await capturedCallback!();

        await _chunkRepository.Received(1)
            .DeleteByDocumentIdAsync(documentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCompleted_Callback_Completes_InnerUoW()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), null);

        Func<Task>? capturedCallback = null;
        _ambientUow
            .When(u => u.OnCompleted(Arg.Any<Func<Task>>()))
            .Do(ci => capturedCallback = ci.Arg<Func<Task>>());

        await _handler.HandleEventAsync(evt);
        await capturedCallback!();

        await _innerUow.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleEventAsync_DoesNothing_When_No_AmbientUoW()
    {
        _uowManager.Current.Returns((IUnitOfWork?)null);

        await _handler.HandleEventAsync(new DocumentDeletingEvent(Guid.NewGuid(), null));

        _ambientUow.DidNotReceive().OnCompleted(Arg.Any<Func<Task>>());
        await _chunkRepository.DidNotReceive()
            .DeleteByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
