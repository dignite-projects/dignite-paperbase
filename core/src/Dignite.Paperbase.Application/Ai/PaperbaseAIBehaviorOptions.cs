namespace Dignite.Paperbase.Ai;

/// <summary>
/// Application-layer behavior knobs for AI workflows (Classification / structured field extraction).
/// Bound to the <c>PaperbaseAIBehavior</c> configuration section in
/// <see cref="PaperbaseApplicationModule"/>.
/// <para>
/// Provider wiring (endpoint / API key / model ids / prompt-cache middleware) lives in the
/// separate <c>PaperbaseAI</c> section consumed by the host's <c>ConfigureAI</c> — keep these
/// two concerns disjoint: this class must not grow connection or credential fields.
/// </para>
/// </summary>
public class PaperbaseAIBehaviorOptions
{
    /// <summary>
    /// 分类提示词中最多包含的候选类型数量，超出时按 Priority 降序截断。
    /// </summary>
    public int MaxDocumentTypesInClassificationPrompt { get; set; } = 50;

    /// <summary>
    /// 结构化提取单次调用最大文本长度，超出时截断。
    /// </summary>
    public int MaxTextLengthPerExtraction { get; set; } = 8000;

    /// <summary>
    /// AI 交互默认语言（影响系统提示词语言）。
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";

    /// <summary>
    /// Title 生成时送入 LLM 的 Markdown 最大字符数。
    /// 超出时截断尾部（文档开头通常已包含标题、摘要等关键信息）。
    /// </summary>
    public int MaxTitleGenerationMarkdownLength { get; set; } = 4000;
}
