namespace Dignite.Paperbase.Chat;

public static class ChatConsts
{
    public static int MaxMessageLength { get; set; } = 4000;
    public static int MaxTitleLength { get; set; } = 200;
    public static int MaxCitationsJsonLength { get; set; } = 8192;

    /// <summary>
    /// Canonical name of the built-in vector search tool exposed to the model.
    /// Used both at registration (see <c>DocumentChatAppService.PrepareAgentSetupAsync</c>)
    /// and at telemetry classification (see <c>DocumentChatTelemetryRecorder.ClassifyGrounding</c>)
    /// so the two stay in sync.
    /// </summary>
    public const string SearchPaperbaseDocumentsToolName = "search_paperbase_documents";
}
