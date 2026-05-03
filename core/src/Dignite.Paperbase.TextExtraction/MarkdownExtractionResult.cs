namespace Dignite.Paperbase.TextExtraction;

public class MarkdownExtractionResult
{
    /// <summary>结构化 Markdown 输出。Provider 未识别到任何内容时为空字符串。</summary>
    public string Markdown { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public string? DetectedLanguage { get; set; }
}
