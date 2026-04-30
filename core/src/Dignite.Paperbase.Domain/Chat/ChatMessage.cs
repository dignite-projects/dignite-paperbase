using System;
using Dignite.Paperbase.Chat;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Dignite.Paperbase.Chat;

public class ChatMessage : Entity<Guid>
{
    public virtual Guid ConversationId { get; private set; }
    public virtual ChatMessageRole Role { get; private set; }
    public virtual string Content { get; private set; } = default!;
    public virtual string? CitationsJson { get; private set; }
    public virtual Guid? ClientTurnId { get; private set; }
    public virtual DateTime CreationTime { get; private set; }

    protected ChatMessage() { }

    internal ChatMessage(
        Guid id,
        Guid conversationId,
        ChatMessageRole role,
        string content,
        string? citationsJson,
        Guid? clientTurnId,
        DateTime creationTime)
        : base(id)
    {
        ConversationId = conversationId;
        Role = role;
        Content = Check.NotNullOrWhiteSpace(content, nameof(content), maxLength: DocumentChatConsts.MaxMessageLength);
        if (citationsJson != null)
            Check.Length(citationsJson, nameof(citationsJson), DocumentChatConsts.MaxCitationsJsonLength);
        CitationsJson = citationsJson;
        ClientTurnId = clientTurnId;
        CreationTime = creationTime;
    }
}
