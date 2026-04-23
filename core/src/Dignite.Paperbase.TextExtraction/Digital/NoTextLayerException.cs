using System;

namespace Dignite.Paperbase.TextExtraction.Digital;

/// <summary>
/// PDF 文件不含文字层时抛出。由 DefaultTextExtractor 捕获后回退到 OCR 路径。
/// </summary>
public class NoTextLayerException : Exception
{
    public NoTextLayerException() : base("PDF has no extractable text layer.") { }
}
