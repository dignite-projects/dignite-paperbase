using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Paperbase.Host.Controllers;

/// <summary>
/// SSE (Server-Sent Events) endpoint for streaming document chat turns.
///
/// Each event is a JSON-serialized <see cref="ChatTurnDeltaDto"/> prefixed with
/// <c>data: </c> per the SSE spec. The stream always terminates with a
/// <see cref="ChatTurnDeltaKind.Done"/> or <see cref="ChatTurnDeltaKind.Error"/> event.
///
/// <para><strong>Authorization note</strong>: The native browser <c>EventSource</c> API
/// does not support custom request headers, so a bearer token cannot be passed that way.
/// Clients must use <c>fetch</c> with <c>ReadableStream</c> or an EventSource polyfill
/// that attaches the token in the <c>Authorization</c> header.
/// OpenIddict's validation middleware accepts bearer tokens in the
/// <c>Authorization: Bearer …</c> header, which all non-native clients can set.</para>
///
/// <para><strong>Middleware note</strong>: This controller is intentionally placed in
/// the host project, not in the core <c>HttpApi</c> project. All SSE / middleware
/// configuration belongs in the host.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/paperbase/document-chat/conversations/{conversationId}/messages/stream")]
public class DocumentChatStreamController : AbpController
{
    private readonly IDocumentChatAppService _appService;

    public DocumentChatStreamController(IDocumentChatAppService appService)
    {
        _appService = appService;
    }

    /// <summary>
    /// Streams the response for a new chat turn as Server-Sent Events.
    /// </summary>
    [HttpPost]
    public virtual async Task StreamAsync(
        Guid conversationId,
        [FromBody] SendChatMessageInput input,
        CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        // Disable proxy/CDN buffering so chunks reach the client immediately.
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var delta in _appService.SendMessageStreamingAsync(
            conversationId, input, cancellationToken))
        {
            var json = JsonSerializer.Serialize(delta);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
