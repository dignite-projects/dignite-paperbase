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
/// Provides prior chat messages for a document-chat conversation, returning them in
/// MAF's <see cref="MeAi.ChatMessage"/> shape ready to be prepended to the next
/// agent turn.
///
/// <para>
/// This is a plain ABP service following the project's <c>*Provider</c> convention
/// (cf. <see cref="IPromptProvider"/>); it does NOT inherit from MAF's
/// <c>ChatHistoryProvider</c> and is not wired into the agent pipeline. Empirically
/// MAF v1.2.0's <c>ChatClientAgent</c> does not auto-load history through that
/// pipeline against our wiring, so <see cref="DocumentChatAppService"/> calls
/// <see cref="GetHistoryAsync"/> directly and prepends the result to
/// <c>ChatClientAgent.RunAsync</c> messages.
/// </para>
///
/// <para>
/// Persistence is owned by the <c>ChatConversation</c> aggregate root, written
/// via <c>ChatConversation.AppendUserMessage</c> / <c>AppendAssistantMessage</c>;
/// this provider is read-only and uses <see cref="IChatConversationRepository"/>
/// (storage backend irrelevant — Postgres in production, SQLite in tests).
/// </para>
/// </summary>
public class DocumentChatHistoryProvider : ITransientDependency
{
    private const int HistoryMessageTake = 50;

    private readonly IServiceScopeFactory _scopeFactory;

    public DocumentChatHistoryProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Returns the most recent <see cref="HistoryMessageTake"/> messages of
    /// <paramref name="conversationId"/> in chronological order. Returns an empty
    /// list if the conversation is missing — callers treat that as "no prior
    /// history" rather than an error.
    /// </summary>
    /// <remarks>
    /// Uses a fresh DI scope per call so the provider can be invoked safely from
    /// background threads / Hangfire / any context where the ambient scope might
    /// be missing or stale.
    /// </remarks>
    public virtual async Task<IReadOnlyList<MeAi.ChatMessage>> GetHistoryAsync(
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
