using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
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

    /// <summary>
    /// Streams a chat turn as a sequence of <see cref="ChatTurnDeltaDto"/> events.
    /// The stream always terminates with either <see cref="ChatTurnDeltaKind.Done"/>
    /// or <see cref="ChatTurnDeltaKind.Error"/>. The same fail-closed authorization
    /// gate and idempotency logic as <see cref="SendMessageAsync"/> apply; an
    /// idempotent replay returns a single <see cref="ChatTurnDeltaKind.Done"/> event.
    /// </summary>
    /// <remarks>
    /// This method is intentionally excluded from the standard ABP auto-API controller.
    /// Use the dedicated SSE endpoint in the host project instead.
    /// </remarks>
    [RemoteService(false)]
    IAsyncEnumerable<ChatTurnDeltaDto> SendMessageStreamingAsync(
        Guid conversationId,
        SendChatMessageInput input,
        CancellationToken cancellationToken = default);

    Task<PagedResultDto<ChatMessageDto>> GetMessageListAsync(Guid conversationId, GetChatMessageListInput input);
}
