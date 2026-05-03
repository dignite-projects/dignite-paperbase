using System.Collections.Generic;

namespace Dignite.Paperbase.Ocr.PaddleOcr;

public class PaddleOcrOptions
{
    /// <summary>PaddleOCR REST 服务地址，默认本地 Docker sidecar。</summary>
    public string Endpoint { get; set; } = "http://localhost:8866";

    /// <summary>
    /// 使用的模型。三种取舍：
    /// <list type="bullet">
    ///   <item><c>PP-StructureV3</c>（默认）—— CPU 可用，输出 Markdown（标题/表格/印章），中文场景最佳。</item>
    ///   <item><c>PP-OCRv4</c> —— 最轻量、纯线级 OCR，无 Markdown 结构化输出。</item>
    ///   <item><c>PaddleOCR-VL-1.5</c> —— VLM 高精度，输出 Markdown，需 GPU。</item>
    /// </list>
    /// 注意：PP-StructureV3 / PaddleOCR-VL 模式下 sidecar 不返回 line-level bbox（每页只产一个 Markdown 块），
    /// <see cref="OcrOptions.IncludeBlockPositions"/> 在这两种模式下被忽略，对应 BoundingBox 始终为零。
    /// </summary>
    public string ModelName { get; set; } = "PP-StructureV3";

    /// <summary>默认识别语言列表（BCP 47），可被 OcrOptions.LanguageHints 覆盖。</summary>
    public IList<string> Languages { get; set; } = new List<string> { "ja", "en" };
}
