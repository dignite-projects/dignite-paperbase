using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents.Chat;

public interface IDocumentChatAppService : IApplicationService
{
    Task<ChatConversationDto> CreateConversationAsync(CreateChatConversationInput input);

    Task<PagedResultDto<ChatConversationListItemDto>> GetConversationListAsync(GetChatConversationListInput input);

    Task<ChatConversationDto> GetConversationAsync(Guid conversationId);

    Task DeleteConversationAsync(Guid conversationId);

    Task<ChatTurnResultDto> SendMessageAsync(Guid conversationId, SendChatMessageInput input);

    Task<PagedResultDto<ChatMessageDto>> GetMessageListAsync(Guid conversationId, GetChatMessageListInput input);
}
