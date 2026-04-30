using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Documents.Chat;

public class ChatTurnDeltaDto
{
    public ChatTurnDeltaKind Kind { get; set; }

    /// <summary>
    /// Incremental text delta. Present when <see cref="Kind"/> is
    /// <see cref="ChatTurnDeltaKind.PartialText"/>. Clients concatenate these chunks
    /// to reconstruct the full answer.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Citations for this turn. Populated in the final <see cref="ChatTurnDeltaKind.Done"/>
    /// event only. Empty list when no documents were retrieved (see <see cref="IsDegraded"/>).
    /// </summary>
    public IList<ChatCitationDto>? Citations { get; set; }

    /// <summary>
    /// Persisted user message id. Set in the <see cref="ChatTurnDeltaKind.Done"/> event.
    /// </summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>
    /// Persisted assistant message id. Set in the <see cref="ChatTurnDeltaKind.Done"/> event.
    /// </summary>
    public Guid? AssistantMessageId { get; set; }

    /// <summary>
    /// True when the model did not invoke the document-search tool (OnDemandFunctionCalling
    /// mode only). The UI should display a "no citations" notice to the user.
    /// Always false in BeforeAIInvoke mode.
    /// </summary>
    public bool IsDegraded { get; set; }

    /// <summary>
    /// Client-safe error message. Present only when <see cref="Kind"/> is
    /// <see cref="ChatTurnDeltaKind.Error"/>. Never contains internal exception details.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
