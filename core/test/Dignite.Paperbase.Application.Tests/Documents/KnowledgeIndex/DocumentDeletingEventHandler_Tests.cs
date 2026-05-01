using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Events;
using Dignite.Paperbase.Documents.KnowledgeIndex;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentDeletingEventHandler_Tests
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IUnitOfWork _ambientUow;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly ILogger<DocumentDeletingEventHandler> _logger;
    private readonly DocumentDeletingEventHandler _handler;

    public DocumentDeletingEventHandler_Tests()
    {
        _ambientUow = Substitute.For<IUnitOfWork>();
        _knowledgeIndex = Substitute.For<IDocumentKnowledgeIndex>();
        _logger = Substitute.For<ILogger<DocumentDeletingEventHandler>>();

        _uowManager = Substitute.For<IUnitOfWorkManager>();
        _uowManager.Current.Returns(_ambientUow);

        _handler = new DocumentDeletingEventHandler(
            _uowManager,
            _knowledgeIndex,
            _logger);
    }

    [Fact]
    public async Task HandleEventAsync_Registers_OnCompleted_Callback()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.HandleEventAsync(evt);

        _ambientUow.Received(1).OnCompleted(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task HandleEventAsync_Does_Not_Delete_Index_Immediately()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.HandleEventAsync(evt);

        await _knowledgeIndex.DidNotReceive()
            .DeleteByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnCompleted_Callback_Deletes_Document_Index()
    {
        var documentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var evt = new DocumentDeletingEvent(documentId, tenantId);

        Func<Task>? capturedCallback = null;
        _ambientUow
            .When(u => u.OnCompleted(Arg.Any<Func<Task>>()))
            .Do(ci => capturedCallback = ci.Arg<Func<Task>>());

        await _handler.HandleEventAsync(evt);

        capturedCallback.ShouldNotBeNull();
        await capturedCallback!();

        await _knowledgeIndex.Received(1)
            .DeleteByDocumentIdAsync(documentId, tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleEventAsync_DoesNothing_When_No_AmbientUoW()
    {
        _uowManager.Current.Returns((IUnitOfWork?)null);

        await _handler.HandleEventAsync(new DocumentDeletingEvent(Guid.NewGuid(), null));

        _ambientUow.DidNotReceive().OnCompleted(Arg.Any<Func<Task>>());
        await _knowledgeIndex.DidNotReceive()
            .DeleteByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        _logger.ReceivedWithAnyArgs(1).Log(
            LogLevel.Warning,
            default,
            default!,
            default,
            default!);
    }

    [Fact]
    public async Task OnCompleted_Callback_Swallows_Index_Delete_Failure()
    {
        var evt = new DocumentDeletingEvent(Guid.NewGuid(), null);
        var expected = new InvalidOperationException("qdrant unavailable");

        _knowledgeIndex
            .DeleteByDocumentIdAsync(evt.DocumentId, evt.TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));

        Func<Task>? capturedCallback = null;
        _ambientUow
            .When(u => u.OnCompleted(Arg.Any<Func<Task>>()))
            .Do(ci => capturedCallback = ci.Arg<Func<Task>>());

        await _handler.HandleEventAsync(evt);

        capturedCallback.ShouldNotBeNull();
        await capturedCallback!();

        _logger.ReceivedWithAnyArgs(1).Log(
            LogLevel.Error,
            default,
            default!,
            default,
            default!);
    }
}
