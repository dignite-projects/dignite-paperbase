namespace Dignite.Paperbase.Ai;

/// <summary>
/// Public constants used by the host wiring (<c>PaperbaseHostModule.ConfigureAI</c>) and
/// the application layer (services consuming keyed AI clients via DI).
/// </summary>
public static class PaperbaseAIConsts
{
    /// <summary>
    /// DI key for the summarizer <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by <c>SummarizationCompactionStrategy</c>. Host registers under this key;
    /// the application layer pulls via <c>[FromKeyedServices(...)]</c>.
    /// Hosts that don't configure a separate summarizer model fall back to the same
    /// underlying chat model — the application layer must accept that arrangement.
    /// </summary>
    public const string SummarizerChatClientKey = "paperbase-summarizer";

    /// <summary>
    /// DI key for the conversation-title-generator <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// used by <c>ChatAppService.TryGenerateAndApplyTitleAsync</c>. Like the summarizer
    /// key, this is a single-shot text-completion path: no tools, no distributed cache
    /// (each prompt is unique), no FunctionInvocation wrapper. Splitting it off from the
    /// main chat client keeps trace structure honest (no phantom <c>orchestrate_tools</c>
    /// spans around a tool-free call) and lets hosts pick a cheaper / faster model for
    /// the title side without dragging the main chat down.
    /// </summary>
    public const string TitleGeneratorChatClientKey = "paperbase-title-generator";
}
