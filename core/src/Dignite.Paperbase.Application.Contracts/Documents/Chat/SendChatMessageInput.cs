using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents.Chat;

public class SendChatMessageInput
{
    [Required]
    [DynamicStringLength(typeof(DocumentChatConsts), nameof(DocumentChatConsts.MaxMessageLength))]
    public string Message { get; set; } = default!;

    /// <summary>
    /// Client-generated identifier for this turn. Required for idempotent retries:
    /// if the same id is replayed, the prior result is returned without re-invoking
    /// the model. Must be unique per conversation.
    /// </summary>
    [Required]
    public Guid ClientTurnId { get; set; }
}
