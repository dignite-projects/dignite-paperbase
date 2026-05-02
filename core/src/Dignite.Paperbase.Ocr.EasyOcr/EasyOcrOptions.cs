using System.Collections.Generic;

namespace Dignite.Paperbase.Ocr.EasyOcr;

public class EasyOcrOptions
{
    /// <summary>EasyOCR REST 服务地址，默认本地 Docker sidecar。</summary>
    public string Endpoint { get; set; } = "http://localhost:8884";

    /// <summary>默认识别语言列表（BCP 47），可被 OcrOptions.LanguageHints 覆盖。</summary>
    public IList<string> Languages { get; set; } = new List<string> { "ja", "en" };
}
