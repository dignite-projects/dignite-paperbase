namespace Dignite.Paperbase.Documents.Chat;

public enum ChatTurnDeltaKind
{
    /// <summary>
    /// An incremental text chunk. <see cref="ChatTurnDeltaDto.Text"/> carries the delta
    /// (not cumulative). Clients concatenate these to build the full answer.
    /// </summary>
    PartialText,

    /// <summary>
    /// Terminal event. Stream is complete; the turn was persisted.
    /// <see cref="ChatTurnDeltaDto.UserMessageId"/>, <see cref="ChatTurnDeltaDto.AssistantMessageId"/>,
    /// and <see cref="ChatTurnDeltaDto.Citations"/> are populated.
    /// </summary>
    Done,

    /// <summary>
    /// Terminal event. An unrecoverable error occurred during streaming.
    /// <see cref="ChatTurnDeltaDto.ErrorMessage"/> carries a safe client-facing message.
    /// No further events follow.
    /// </summary>
    Error
}
