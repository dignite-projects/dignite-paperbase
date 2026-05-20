namespace Dignite.Paperbase.Ai;

/// <summary>
/// Public constants used by the host wiring (<c>PaperbaseHostModule.ConfigureAI</c>) and
/// every service across core + business modules that consumes a keyed AI client via DI.
///
/// <para>
/// Lives in <c>Dignite.Paperbase.Abstractions</c> so business modules (whose Domain layer
/// references only Abstractions, not Application) can also inject these keyed clients.
/// See <c>docs/ai-provider.md</c> for the keyed-clients table and what each is for.
/// </para>
/// </summary>
public static class PaperbaseAIConsts
{
    /// <summary>
    /// DI key for the document-title-generator <c>IChatClient</c>
    /// used by <c>DocumentTextExtractionBackgroundJob.TryGenerateTitleAsync</c>. This is a
    /// single-shot text-completion path: no tools, no distributed cache (each prompt is
    /// unique), no FunctionInvocation wrapper. Splitting it off from the main chat client
    /// keeps trace structure honest (no phantom <c>orchestrate_tools</c> spans around a
    /// tool-free call) and lets hosts pick a cheaper / faster model for the title side.
    /// </summary>
    public const string TitleGeneratorChatClientKey = "paperbase-title-generator";

    /// <summary>
    /// DI key for the structured-output <c>IChatClient</c>
    /// shared by all single-shot, tool-free, prompt-unique <c>RunAsync&lt;T&gt;</c> call
    /// sites: <c>DocumentClassificationWorkflow</c> and business-module field extractors.
    ///
    /// <para>
    /// Same shape as the title-generator client (no FunctionInvocation, no DistributedCache) —
    /// these calls do not invoke tools, their prompts are document-content-derived (unique
    /// per call), and their outputs are schema-bound by
    /// <c>ChatResponseFormat.ForJsonSchema&lt;T&gt;</c>. Wrapping them with
    /// FunctionInvocation just produces phantom <c>orchestrate_tools</c> spans on traces,
    /// and DistributedCache lookups always miss.
    /// </para>
    ///
    /// <para>
    /// Hosts that want per-task model tuning (e.g. small fast model for classification,
    /// stronger model for field extraction) can override <c>ConfigureAI</c> to register
    /// additional per-purpose keyed clients on top of this default consolidation.
    /// </para>
    /// </summary>
    public const string StructuredChatClientKey = "paperbase-structured";
}
