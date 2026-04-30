using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Chat;

public class ChatConversationListItemDto : EntityDto<Guid>
{
    public string Title { get; set; } = default!;

    public Guid? DocumentId { get; set; }

    public string? DocumentTypeCode { get; set; }

    public DateTime CreationTime { get; set; }
}
