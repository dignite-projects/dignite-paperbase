using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using MeAi = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

public class DocumentChatHistoryLoader_Tests
    : PaperbaseApplicationTestBase<DocumentChatAppServiceTestModule>
{
    private readonly IChatConversationRepository _repository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Volo.Abp.Timing.IClock _clock;

    public DocumentChatHistoryLoader_Tests()
    {
        _repository = GetRequiredService<IChatConversationRepository>();
        _scopeFactory = GetRequiredService<IServiceScopeFactory>();
        _clock = GetRequiredService<Volo.Abp.Timing.IClock>();
    }

    [Fact]
    public async Task Should_Load_History_From_Conversation_Repository()
    {
        var conversationId = await CreateConversationWithMessagesAsync();
        var loader = new DocumentChatHistoryLoader(_scopeFactory);

        var messages = (await loader.LoadAsync(conversationId)).ToList();

        messages.Count.ShouldBe(2);
        messages[0].Role.ShouldBe(ChatRole.User);
        messages[0].Text.ShouldBe("hello");
        messages[1].Role.ShouldBe(ChatRole.Assistant);
        messages[1].Text.ShouldBe("hi");
    }

    [Fact]
    public async Task Should_Return_Empty_When_Conversation_Missing()
    {
        // Conversation id never inserted — loader treats this as "no prior history"
        // rather than throwing, so the chat AppService can start a fresh turn cleanly
        // even if the conversation has been deleted between authorization and load.
        var loader = new DocumentChatHistoryLoader(_scopeFactory);

        var messages = await loader.LoadAsync(Guid.NewGuid());

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Use_Fresh_DI_Scope_Per_Call()
    {
        // The loader is invoked from possibly background / non-HTTP contexts where the
        // ambient scope may be missing or stale; each call must create its own scope so
        // the IChatConversationRepository resolution is always valid.
        var conversationId = Guid.NewGuid();
        var conversation = new ChatConversation(
            conversationId,
            tenantId: null,
            title: "Test",
            documentId: null,
            documentTypeCode: "contract.general",
            topK: null,
            minScore: null);

        var repository = Substitute.For<IChatConversationRepository>();
        repository.FindByIdWithMessagesAsync(
                conversationId,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(conversation);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChatConversationRepository)).Returns(repository);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var loader = new DocumentChatHistoryLoader(scopeFactory);

        await loader.LoadAsync(conversationId);
        await loader.LoadAsync(conversationId);

        scopeFactory.Received(2).CreateScope();
    }

    [Fact]
    public async Task Should_Return_Messages_In_Chronological_Order()
    {
        // Caller (DocumentChatAppService) prepends history to the new user message and
        // passes the lot to RunAsync; ordering must be ascending by creation time so the
        // LLM sees the conversation as it actually unfolded.
        var conversationId = await CreateConversationWithMessagesAsync();
        var loader = new DocumentChatHistoryLoader(_scopeFactory);

        var messages = (await loader.LoadAsync(conversationId)).ToList();

        for (var i = 1; i < messages.Count; i++)
        {
            // CreationTime ordering can't be observed on MeAi.ChatMessage directly, but
            // role alternation (user → assistant) of the seeded data is the proxy.
            messages[i - 1].Role.ShouldBe(ChatRole.User);
            messages[i].Role.ShouldBe(ChatRole.Assistant);
        }
    }

    private async Task<Guid> CreateConversationWithMessagesAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var conversation = new ChatConversation(
                Guid.NewGuid(),
                tenantId: null,
                title: "Test",
                documentId: null,
                documentTypeCode: "contract.general",
                topK: null,
                minScore: null);

            conversation.AppendUserMessage(_clock, Guid.NewGuid(), "hello", Guid.NewGuid());
            conversation.AppendAssistantMessage(_clock, Guid.NewGuid(), "hi", citationsJson: null);
            await _repository.InsertAsync(conversation, autoSave: true);

            return conversation.Id;
        });
    }
}
