namespace Dignite.Paperbase.Documents;

public enum SourceType
{
    /// <summary>纸质文档扫描件（图片格式或扫描 PDF），需要 OCR 提取文本</summary>
    Physical = 1,

    /// <summary>数字原生文件（带文字层的 PDF、Word、Markdown 等），直接提取文本</summary>
    Digital = 2
}
