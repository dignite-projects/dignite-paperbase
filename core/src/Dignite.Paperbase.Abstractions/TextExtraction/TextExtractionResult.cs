namespace Dignite.Paperbase.Abstractions.TextExtraction;

public class TextExtractionResult
{
    public string ExtractedText { get; set; } = default!;
    public double Confidence { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    /// <summary>true = OCR (physical scan), false = direct text layer (digital)</summary>
    public bool UsedOcr { get; set; }
}
