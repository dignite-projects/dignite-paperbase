using System;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// State carried in <see cref="Microsoft.Agents.AI.AgentSession.StateBag"/> by
/// <see cref="DocumentChatAppService"/> so <see cref="DocumentChatHistoryProvider"/>
/// can resolve the conversation aggregate to load history from.
///
/// <para>
/// Class (not record struct) is required because <see cref="Microsoft.Agents.AI.AgentSessionStateBag.SetValue{T}"/>
/// is constrained to <c>T : class</c>. JSON-serializable via the parameterless constructor
/// + public setter — required by <c>AgentSessionStateBagJsonConverter</c>.
/// </para>
/// </summary>
public sealed class DocumentChatSessionState
{
    public DocumentChatSessionState() { }

    public DocumentChatSessionState(Guid conversationId)
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; set; }
}
