using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Chat;

public class ChatTurnResultDto
{
    public Guid UserMessageId { get; set; }

    public Guid AssistantMessageId { get; set; }

    public string Answer { get; set; } = default!;

    public IList<ChatCitationDto> Citations { get; set; } = new List<ChatCitationDto>();

    /// <summary>
    /// True when the model declined to invoke ANY tool in this turn, so the answer
    /// has no traceable grounding (vector or structured). Equivalent to
    /// <c>GroundingSource == GroundingSource.None</c>. The UI should surface a
    /// "no sources used" notice when this is true.
    /// </summary>
    /// <remarks>
    /// Issue #99 redefined this from "<c>!HasSearches</c>" (vector-only signal) to
    /// "<c>HasAnyToolCall == false</c>". A turn that answered using only structured
    /// business tools (e.g. <c>get_contract_aggregate</c>) was previously misclassified
    /// as degraded; that misclassification is fixed by deriving from
    /// <see cref="GroundingSource"/>.
    /// </remarks>
    public bool IsDegraded { get; set; }

    /// <summary>
    /// Categorizes which kinds of tools the model invoked to produce this answer
    /// (vector search vs. structured business tools vs. both vs. none). Lets the UI
    /// distinguish "grounded via RAG" from "grounded via business data" rather than
    /// only knowing whether grounding happened at all.
    /// </summary>
    public GroundingSource GroundingSource { get; set; }
}
