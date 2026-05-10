using System;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// State carried in <see cref="Microsoft.Agents.AI.AgentSession.StateBag"/> by
/// <see cref="ChatAppService"/> so <see cref="ConversationHistoryProvider"/>
/// can resolve the conversation aggregate to load history from.
///
/// <para>
/// Class (not record struct) is required because <see cref="Microsoft.Agents.AI.AgentSessionStateBag.SetValue{T}"/>
/// is constrained to <c>T : class</c>. JSON-serializable via the parameterless constructor
/// + public setter — required by <c>AgentSessionStateBagJsonConverter</c>.
/// </para>
/// </summary>
public sealed class ChatSessionState
{
    public ChatSessionState() { }

    public ChatSessionState(Guid conversationId)
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; set; }
}
