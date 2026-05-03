namespace Dignite.Paperbase.Abstractions.TextExtraction;

public class TextExtractionResult
{
    /// <summary>
    /// 结构化 Markdown 输出。Provider 未识别到任何内容时为空字符串。
    /// 这是 TextExtraction 流水线的唯一文本载荷——下游需要纯文本时通过
    /// <see cref="Dignite.Paperbase.Documents.MarkdownStripper"/> 投影。
    /// </summary>
    public string Markdown { get; set; } = string.Empty;

    public double Confidence { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    /// <summary>true = OCR (physical scan), false = direct text layer (digital)</summary>
    public bool UsedOcr { get; set; }
}
