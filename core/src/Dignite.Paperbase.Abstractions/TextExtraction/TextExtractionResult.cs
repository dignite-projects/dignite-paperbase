namespace Dignite.Paperbase.Abstractions.TextExtraction;

public class TextExtractionResult
{
    public string ExtractedText { get; set; } = default!;
    public double Confidence { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    /// <summary>true = OCR (physical scan), false = direct text layer (digital)</summary>
    public bool UsedOcr { get; set; }

    /// <summary>结构化 Markdown 输出，仅支持 Markdown 的 Provider 填充；其他保持 null。</summary>
    public string? Markdown { get; set; }
}
