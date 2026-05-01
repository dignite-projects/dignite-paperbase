namespace Dignite.Paperbase.Ai;

/// <summary>
/// Controls when the <c>TextSearchProvider</c> retrieves document context during
/// a chat turn.
/// </summary>
public enum ChatSearchBehavior
{
    /// <summary>
    /// Default. Retrieval runs automatically before every AI invocation; results are
    /// injected as additional context messages. Citations are always populated.
    /// </summary>
    BeforeAIInvoke,

    /// <summary>
    /// Retrieval is exposed as a function tool the model may call.
    /// Saves tokens when the model determines no retrieval is needed, but may yield
    /// an empty citation list (<see cref="ChatTurnResultDto.IsDegraded"/> = true)
    /// when the model elects not to invoke the tool.
    /// </summary>
    OnDemandFunctionCalling
}
