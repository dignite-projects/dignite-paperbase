namespace Dignite.Paperbase.Ocr;

/// <summary>
/// Provider-neutral OCR quality diagnostics derived from the full-file OCR Markdown.
/// Consumed by the quality scorer to decide whether one targeted retry is warranted.
/// </summary>
public class OcrQualitySignals
{
    public double? Confidence { get; set; }
    public int PageCount { get; set; }
    public int MarkdownLength { get; set; }
    public bool HasMeaningfulText { get; set; }
    public int TableMarkerCount { get; set; }
    public int FormLikeLineCount { get; set; }
}
