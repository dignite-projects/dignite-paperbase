using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Documents.Chat;

public class ChatTurnResultDto
{
    public Guid UserMessageId { get; set; }

    public Guid AssistantMessageId { get; set; }

    public string Answer { get; set; } = default!;

    public IList<ChatCitationDto> Citations { get; set; } = new List<ChatCitationDto>();

    public bool IsDegraded { get; set; }
}
