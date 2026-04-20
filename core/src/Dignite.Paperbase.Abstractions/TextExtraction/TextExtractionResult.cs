using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.TextExtraction;

public class TextExtractionResult
{
    public string ExtractedText { get; set; } = default!;
    public double Confidence { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    /// <summary>
    /// 能力私有元数据（OCR Provider 名、Token 成本、耗时等）。
    /// 核心模块写入 DocumentPipelineRun.Metadata，不解析具体内容。
    /// </summary>
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
