using System.Collections.Generic;

namespace Dignite.Paperbase.Ocr.PaddleOcr;

public class PaddleOcrOptions
{
    /// <summary>PaddleOCR REST 服务地址，默认本地 Docker sidecar。</summary>
    public string Endpoint { get; set; } = "http://localhost:8866";

    /// <summary>使用的模型，可选 PP-OCRv4（默认）或 PaddleOCR-VL-1.5（高精度，需 GPU）。</summary>
    public string ModelName { get; set; } = "PP-OCRv4";

    /// <summary>默认识别语言列表（BCP 47），可被 OcrOptions.LanguageHints 覆盖。</summary>
    public IList<string> Languages { get; set; } = new List<string> { "ja", "en" };
}
