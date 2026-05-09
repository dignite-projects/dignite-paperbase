using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Chat;

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
    /// True when the model declined to invoke ANY tool in this turn, so the answer
    /// has no traceable grounding. Equivalent to
    /// <c>GroundingSource == GroundingSource.None</c>. The UI should surface a
    /// "no sources used" notice when this is true. Set on the
    /// <see cref="ChatTurnDeltaKind.Done"/> event only.
    /// </summary>
    public bool IsDegraded { get; set; }

    /// <summary>
    /// Categorizes which kinds of tools the model invoked (vector search vs. structured
    /// business tools vs. both vs. none). Set on the <see cref="ChatTurnDeltaKind.Done"/>
    /// event only. See <see cref="ChatTurnResultDto.GroundingSource"/>.
    /// </summary>
    public GroundingSource GroundingSource { get; set; }

    /// <summary>
    /// Client-safe error message. Present only when <see cref="Kind"/> is
    /// <see cref="ChatTurnDeltaKind.Error"/>. Never contains internal exception details.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
