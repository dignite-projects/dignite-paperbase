using System.Collections.Generic;

namespace Dignite.Paperbase.Ocr;

public class OcrResult
{
    /// <summary>提取的完整文本，段落间以换行符分隔。</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>带坐标的文本块列表（仅当 OcrOptions.IncludeBlockPositions = true 时填充）。</summary>
    public IList<OcrBlock> Blocks { get; set; } = new List<OcrBlock>();

    /// <summary>整体识别置信度（0.0 ~ 1.0）。</summary>
    public double Confidence { get; set; }

    /// <summary>检测到的主要语言（BCP 47 格式）。</summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>识别的页数。</summary>
    public int PageCount { get; set; }

    /// <summary>结构化 Markdown 输出（可选），未提供时为 null。PP-StructureV3 / PaddleOCR-VL / Azure DI 等支持 Markdown 输出的 Provider 可填充。</summary>
    public string? Markdown { get; set; }
}

public class OcrBlock
{
    public string Text { get; set; } = string.Empty;
    public BoundingBox BoundingBox { get; set; } = new();
    public int PageNumber { get; set; }
    public double Confidence { get; set; }
}

public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
