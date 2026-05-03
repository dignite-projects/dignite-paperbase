namespace Dignite.Paperbase.TextExtraction;

public class MarkdownExtractionResult
{
    /// <summary>纯文本（去除 Markdown 标记）。Provider 未识别到任何内容时为空字符串。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>结构化 Markdown 输出。Provider 可填充；未填充时为 null。</summary>
    public string? Markdown { get; set; }

    public int PageCount { get; set; }

    public string? DetectedLanguage { get; set; }
}
