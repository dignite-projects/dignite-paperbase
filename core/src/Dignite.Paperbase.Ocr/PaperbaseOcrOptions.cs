using System.Collections.Generic;

namespace Dignite.Paperbase.Ocr;

public class PaperbaseOcrOptions
{
    /// <summary>默认语言提示，应用于所有 OCR 请求（BCP 47 格式）。</summary>
    public IList<string> DefaultLanguageHints { get; set; } = new List<string> { "ja", "en" };
}
