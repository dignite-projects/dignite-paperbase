using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;

namespace Dignite.Paperbase.Host.Pages.DocumentChat;

/// <summary>
/// Dev-only smoke-test page for the doc-chat feature. Never mounted in production
/// (AddRazorPages is conditionally called only in IsDevelopment in PaperbaseHostModule).
/// Calls IDocumentChatAppService directly via DI — no internal HTTP round-trip.
/// </summary>
[Authorize]
public class IndexModel : PageModel
{
    private readonly IDocumentChatAppService _chatService;
    private readonly IWebHostEnvironment _hostingEnvironment;

    public PagedResultDto<ChatConversationListItemDto> Conversations { get; private set; } =
        new(0, new List<ChatConversationListItemDto>());

    public ChatConversationDto? ActiveConversation { get; private set; }

    public List<ChatMessageDto> Messages { get; private set; } = new();

    /// <summary>Error set when the requested conversation is not found or unauthorized.</summary>
    public string? NotFoundError { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ConversationId { get; set; }

    public IndexModel(
        IDocumentChatAppService chatService,
        IWebHostEnvironment hostingEnvironment)
    {
        _chatService = chatService;
        _hostingEnvironment = hostingEnvironment;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_hostingEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        Conversations = await _chatService.GetConversationListAsync(new GetChatConversationListInput
        {
            MaxResultCount = 10,
            Sorting = "CreationTime DESC"
        });

        if (ConversationId.HasValue)
        {
            try
            {
                ActiveConversation = await _chatService.GetConversationAsync(ConversationId.Value);
                var result = await _chatService.GetMessageListAsync(
                    ConversationId.Value,
                    new GetChatMessageListInput { MaxResultCount = 50, Sorting = "CreationTime ASC" });
                Messages = new List<ChatMessageDto>(result.Items);
            }
            catch (EntityNotFoundException)
            {
                NotFoundError = "Conversation not found.";
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? title, string? documentTypeCode)
    {
        if (!_hostingEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        var conversation = await _chatService.CreateConversationAsync(new CreateChatConversationInput
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            DocumentTypeCode = string.IsNullOrWhiteSpace(documentTypeCode) ? null : documentTypeCode,
        });
        return RedirectToPage(new { conversationId = conversation.Id });
    }

    /// <summary>
    /// AJAX endpoint. Accepts JSON body; caller must pass the anti-forgery token in the
    /// RequestVerificationToken header (read from the hidden __RequestVerificationToken
    /// input that the page renders via @Html.AntiForgeryToken()).
    /// Returns 200 with turn result JSON, or 409 on concurrent-stamp conflict.
    /// </summary>
    public async Task<IActionResult> OnPostSendMessageAsync([FromBody] SendMessageRequest request)
    {
        if (!_hostingEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            var result = await _chatService.SendMessageAsync(request.ConversationId, new SendChatMessageInput
            {
                Message = request.Message,
                ClientTurnId = request.ClientTurnId
            });

            return new JsonResult(new
            {
                success = true,
                answer = result.Answer,
                citations = result.Citations,
                isDegraded = result.IsDegraded
            });
        }
        catch (EntityNotFoundException)
        {
            Response.StatusCode = 404;
            return new JsonResult(new { success = false, error = "not_found" });
        }
        catch (AbpDbConcurrencyException)
        {
            Response.StatusCode = 409;
            return new JsonResult(new { success = false, error = "conflict" });
        }
    }

    public record SendMessageRequest(Guid ConversationId, string Message, Guid ClientTurnId);
}
