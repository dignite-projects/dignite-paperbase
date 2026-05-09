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

/// <summary>
/// MAF <see cref="ChatHistoryProvider"/> backed by the <c>ChatConversation</c> aggregate.
/// Read-only: persistence (including <c>CitationsJson</c> and <c>IsDegraded</c>, neither
/// of which exists on <see cref="MeAi.ChatMessage"/>) is owned by
/// <see cref="DocumentChatAppService"/> via <c>ChatConversation.AppendUserMessage</c>
/// / <c>AppendAssistantMessage</c>; <see cref="StoreChatHistoryAsync"/> stays at the
/// base class no-op default.
///
/// <para>
/// The conversation id is taken from <see cref="AgentSession.StateBag"/> under
/// <see cref="SessionStateKey"/> rather than ambient context, because per the
/// <see cref="ChatHistoryProvider"/> contract a provider instance must be safe to
/// share across many sessions.
/// </para>
/// </summary>
public class DocumentChatHistoryProvider : ChatHistoryProvider, ITransientDependency
{
    public const string SessionStateKey = "Paperbase.DocumentChat";

    protected virtual int MaxHistoryMessages => 50;

    private readonly IServiceScopeFactory _scopeFactory;

    public DocumentChatHistoryProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A fresh DI scope is used per call so the provider can be invoked from background
    /// or non-HTTP contexts where the ambient scope might be missing or stale.
    /// Returns an empty enumerable when the StateBag has no conversation id, or when the
    /// conversation has been deleted between authorization and load — callers treat that
    /// as "no prior history" rather than an error.
    /// </remarks>
    protected override async ValueTask<IEnumerable<MeAi.ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session;
        if (session is null)
        {
            return [];
        }

        var state = session.StateBag.GetValue<DocumentChatSessionState>(SessionStateKey);
        if (state is null || state.ConversationId == Guid.Empty)
        {
            return [];
        }

        return await LoadHistoryAsync(state.ConversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads history without going through the MAF invocation pipeline. Exposed as
    /// <c>internal virtual</c> so tests can exercise the database adapter directly
    /// without constructing an <see cref="InvokingContext"/> (which carries the
    /// experimental <c>MAAI001</c> diagnostic).
    /// </summary>
    internal virtual async Task<IReadOnlyList<MeAi.ChatMessage>> LoadHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChatConversationRepository>();

        var conversation = await repository.FindByIdWithMessagesAsync(
            conversationId,
            MaxHistoryMessages,
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
