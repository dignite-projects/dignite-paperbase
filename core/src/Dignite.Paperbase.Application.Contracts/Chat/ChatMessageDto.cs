using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Chat;

public class ChatMessageDto : EntityDto<Guid>
{
    public Guid ConversationId { get; set; }

    public ChatMessageRole Role { get; set; }

    public string Content { get; set; } = default!;

    public string? CitationsJson { get; set; }

    public Guid? ClientTurnId { get; set; }

    public DateTime CreationTime { get; set; }
}
