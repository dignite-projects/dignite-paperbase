using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.Ocr;

public class OcrOptions
{
    /// <summary>BCP 47 语言提示，如 ["ja", "en"]。</summary>
    public IList<string> LanguageHints { get; set; } = new List<string>();
}
