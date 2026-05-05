using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using MeAi = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Loads prior chat messages for a document-chat conversation, returning them in
/// MAF's <see cref="MeAi.ChatMessage"/> shape ready to be prepended to the next
/// agent turn.
///
/// <para>
/// This is a plain ABP service, not a MAF <c>ChatHistoryProvider</c>. The
/// <c>ChatClientAgent</c> in our wiring does not auto-load history through the
/// provider pipeline (verified empirically against MAF v1.2.0); the
/// <see cref="DocumentChatAppService"/> calls <see cref="LoadAsync"/> directly
/// and prepends the result to <c>ChatClientAgent.RunAsync</c> messages.
/// </para>
///
/// <para>
/// Persistence is owned by the <c>ChatConversation</c> aggregate root, written
/// via <c>ChatConversation.AppendUserMessage</c> / <c>AppendAssistantMessage</c>;
/// this loader is read-only and uses <see cref="IChatConversationRepository"/>
/// (storage backend irrelevant — Postgres in production, SQLite in tests).
/// </para>
/// </summary>
public class DocumentChatHistoryLoader : ITransientDependency
{
    private const int HistoryMessageTake = 50;

    private readonly IServiceScopeFactory _scopeFactory;

    public DocumentChatHistoryLoader(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Loads the most recent <see cref="HistoryMessageTake"/> messages of
    /// <paramref name="conversationId"/> in chronological order. Returns an empty
    /// list if the conversation is missing — callers treat that as "no prior
    /// history" rather than an error.
    /// </summary>
    /// <remarks>
    /// Uses a fresh DI scope per call so the loader can be invoked safely from
    /// background threads / Hangfire / any context where the ambient scope might
    /// be missing or stale.
    /// </remarks>
    public virtual async Task<IReadOnlyList<MeAi.ChatMessage>> LoadAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
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
