using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Chat;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.HttpApi.Documents;

[Area("paperbase")]
[Route("api/paperbase/document-chat")]
public class DocumentChatController : PaperbaseController, IDocumentChatAppService
{
    private readonly IDocumentChatAppService _documentChatAppService;

    public DocumentChatController(IDocumentChatAppService documentChatAppService)
    {
        _documentChatAppService = documentChatAppService;
    }

    [HttpPost("conversations")]
    public virtual Task<ChatConversationDto> CreateConversationAsync(
        [FromBody] CreateChatConversationInput input)
    {
        return _documentChatAppService.CreateConversationAsync(input);
    }

    [HttpGet("conversations")]
    public virtual Task<PagedResultDto<ChatConversationListItemDto>> GetConversationListAsync(
        [FromQuery] GetChatConversationListInput input)
    {
        return _documentChatAppService.GetConversationListAsync(input);
    }

    [HttpGet("conversations/{conversationId}")]
    public virtual Task<ChatConversationDto> GetConversationAsync(Guid conversationId)
    {
        return _documentChatAppService.GetConversationAsync(conversationId);
    }

    [HttpDelete("conversations/{conversationId}")]
    public virtual Task DeleteConversationAsync(Guid conversationId)
    {
        return _documentChatAppService.DeleteConversationAsync(conversationId);
    }

    [HttpPost("conversations/{conversationId}/messages")]
    public virtual Task<ChatTurnResultDto> SendMessageAsync(
        Guid conversationId,
        [FromBody] SendChatMessageInput input)
    {
        return _documentChatAppService.SendMessageAsync(conversationId, input);
    }

    [HttpGet("conversations/{conversationId}/messages")]
    public virtual Task<PagedResultDto<ChatMessageDto>> GetMessageListAsync(
        Guid conversationId,
        [FromQuery] GetChatMessageListInput input)
    {
        return _documentChatAppService.GetMessageListAsync(conversationId, input);
    }
}
