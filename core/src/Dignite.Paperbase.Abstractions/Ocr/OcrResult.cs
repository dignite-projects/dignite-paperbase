using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.Ocr;

public class OcrResult
{
    /// <summary>所有页面拼接后的全文（换行分隔）。</summary>
    public string FullText { get; set; } = string.Empty;

    public IList<OcrPage> Pages { get; set; } = new List<OcrPage>();

    /// <summary>Provider 元数据（ProviderName、ModelId、LatencyMs 等）。</summary>
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

public class OcrPage
{
    public int PageNumber { get; set; }
    public IList<OcrBlock> Blocks { get; set; } = new List<OcrBlock>();
}

public class OcrBlock
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
