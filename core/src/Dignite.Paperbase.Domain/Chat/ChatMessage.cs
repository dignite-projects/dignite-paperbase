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
    public virtual bool IsDegraded { get; private set; }
    public virtual Guid? ClientTurnId { get; private set; }
    public virtual DateTime CreationTime { get; private set; }

    protected ChatMessage() { }

    internal ChatMessage(
        Guid id,
        Guid conversationId,
        ChatMessageRole role,
        string content,
        string? citationsJson,
        bool isDegraded,
        Guid? clientTurnId,
        DateTime creationTime)
        : base(id)
    {
        ConversationId = conversationId;
        Role = role;
        Content = Check.NotNullOrWhiteSpace(content, nameof(content), maxLength: ChatConsts.MaxMessageLength);
        if (citationsJson != null)
            Check.Length(citationsJson, nameof(citationsJson), ChatConsts.MaxCitationsJsonLength);
        CitationsJson = citationsJson;
        IsDegraded = isDegraded;
        ClientTurnId = clientTurnId;
        CreationTime = creationTime;
    }
}
