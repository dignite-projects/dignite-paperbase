using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using MeAi = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

public class PaperbasePostgresChatHistoryProvider : ChatHistoryProvider, ITransientDependency
{
    public const string ConversationIdStateKey = "Paperbase.DocumentChat.ConversationId";

    private const int HistoryMessageTake = 50;

    private readonly IServiceScopeFactory _scopeFactory;

    public PaperbasePostgresChatHistoryProvider(
        IServiceScopeFactory scopeFactory,
        Func<IEnumerable<MeAi.ChatMessage>, IEnumerable<MeAi.ChatMessage>>? truncate = null,
        Func<IEnumerable<MeAi.ChatMessage>, IEnumerable<MeAi.ChatMessage>>? prepare = null)
        : base(truncate, prepare, storeInputResponseMessageFilter: null)
    {
        _scopeFactory = scopeFactory;
    }

    public override IReadOnlyList<string> StateKeys { get; } = [ConversationIdStateKey];

    public virtual async Task<IReadOnlyList<MeAi.ChatMessage>> GetChatHistoryAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        return (await LoadChatHistoryAsync(session, cancellationToken)).ToList();
    }

    protected override async ValueTask<IEnumerable<MeAi.ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session ?? throw new InvalidOperationException(
            "A chat history provider invocation must include an AgentSession.");

        return await LoadChatHistoryAsync(session, cancellationToken);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    private async Task<IEnumerable<MeAi.ChatMessage>> LoadChatHistoryAsync(
        AgentSession session,
        CancellationToken cancellationToken)
    {
        if (!session.StateBag.TryGetValue<string>(
                ConversationIdStateKey,
                out var conversationIdText))
        {
            throw new InvalidOperationException(
                $"Missing chat conversation id in AgentSession.StateBag['{ConversationIdStateKey}'].");
        }

        if (!Guid.TryParse(conversationIdText, out var conversationId))
        {
            throw new InvalidOperationException(
                $"Invalid chat conversation id in AgentSession.StateBag['{ConversationIdStateKey}'].");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChatConversationRepository>();

        var conversation = await repository.FindByIdWithMessagesAsync(
            conversationId,
            HistoryMessageTake,
            cancellationToken);

        if (conversation is null)
        {
            return [];
        }

        return conversation.Messages
            .OrderBy(m => m.CreationTime)
            .Select(ToAiMessage)
            .ToList();
    }

    private static MeAi.ChatMessage ToAiMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatMessageRole.User => MeAi.ChatRole.User,
            ChatMessageRole.Assistant => MeAi.ChatRole.Assistant,
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, null)
        };

        return new MeAi.ChatMessage(role, message.Content);
    }
}
