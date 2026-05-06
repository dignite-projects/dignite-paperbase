using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Chat;

public class GetChatConversationListInput : PagedAndSortedResultRequestDto
{
    public Guid? DocumentId { get; set; }
}
