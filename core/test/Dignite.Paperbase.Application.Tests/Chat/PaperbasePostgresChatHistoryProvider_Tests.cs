using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using MeAi = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

public class PaperbasePostgresChatHistoryProvider_Tests
    : PaperbaseApplicationTestBase<DocumentChatAppServiceTestModule>
{
    private readonly IChatConversationRepository _repository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Volo.Abp.Timing.IClock _clock;

    public PaperbasePostgresChatHistoryProvider_Tests()
    {
        _repository = GetRequiredService<IChatConversationRepository>();
        _scopeFactory = GetRequiredService<IServiceScopeFactory>();
        _clock = GetRequiredService<Volo.Abp.Timing.IClock>();
    }

    [Fact]
    public async Task Should_Read_History_From_Conversation_Repository()
    {
        var conversationId = await CreateConversationWithMessagesAsync();
        var provider = new TestableProvider(_scopeFactory);
        var session = CreateSession(conversationId);

        var messages = (await provider.ProvideAsync(session)).ToList();

        messages.Count.ShouldBe(2);
        messages[0].Role.ShouldBe(ChatRole.User);
        messages[0].Text.ShouldBe("hello");
        messages[1].Role.ShouldBe(ChatRole.Assistant);
        messages[1].Text.ShouldBe("hi");
    }

    [Fact]
    public async Task Should_Be_Noop_On_Store()
    {
        var conversationId = await CreateConversationWithMessagesAsync();
        var provider = new TestableProvider(_scopeFactory);
        var session = CreateSession(conversationId);

        await provider.StoreAsync(
            session,
            [new MeAi.ChatMessage(ChatRole.User, "new user")],
            [new MeAi.ChatMessage(ChatRole.Assistant, "new assistant")]);

        var conversation = await WithUnitOfWorkAsync(async () =>
            await _repository.FindByIdWithMessagesAsync(conversationId, 50));

        conversation!.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Use_Fresh_DbContext_Per_Call()
    {
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

        var provider = new TestableProvider(scopeFactory);
        var session = CreateSession(conversationId);

        await provider.ProvideAsync(session);
        await provider.ProvideAsync(session);

        scopeFactory.Received(2).CreateScope();
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

    private static TestAgentSession CreateSession(Guid conversationId)
    {
        var session = new TestAgentSession();
        session.StateBag.SetValue(
            PaperbasePostgresChatHistoryProvider.ConversationIdStateKey,
            conversationId.ToString());
        return session;
    }

    private sealed class TestAgentSession : AgentSession
    {
    }

    private sealed class TestableProvider : PaperbasePostgresChatHistoryProvider
    {
        private static readonly TestAgent Agent = new();

        public TestableProvider(IServiceScopeFactory scopeFactory)
            : base(scopeFactory)
        {
        }

        public async Task<IEnumerable<MeAi.ChatMessage>> ProvideAsync(AgentSession session)
        {
#pragma warning disable MAAI001
            var context = new InvokingContext(Agent, session, []);
#pragma warning restore MAAI001
            return await ProvideChatHistoryAsync(context, CancellationToken.None);
        }

        public async Task StoreAsync(
            AgentSession session,
            IEnumerable<MeAi.ChatMessage> requestMessages,
            IEnumerable<MeAi.ChatMessage> responseMessages)
        {
#pragma warning disable MAAI001
            var context = new InvokedContext(Agent, session, requestMessages, responseMessages);
#pragma warning restore MAAI001
            await StoreChatHistoryAsync(context, CancellationToken.None);
        }
    }

    private sealed class TestAgent : AIAgent
    {
        protected override ValueTask<AgentSession> CreateSessionCoreAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<AgentSession>(new TestAgentSession());
        }

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<JsonElement>(session.StateBag.Serialize());
        }

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedSession,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<AgentSession>(new TestAgentSession());
        }

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<MeAi.ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<MeAi.ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
