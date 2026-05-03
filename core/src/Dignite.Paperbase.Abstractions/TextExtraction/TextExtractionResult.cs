namespace Dignite.Paperbase.Abstractions.TextExtraction;

public class TextExtractionResult
{
    /// <summary>
    /// 结构化 Markdown 输出。Provider 未识别到任何内容时为空字符串。
    /// 这是 TextExtraction 流水线的<b>唯一</b>文本载荷——下游需要纯文本时通过
    /// <see cref="Dignite.Paperbase.Documents.MarkdownStripper"/> 投影。
    /// </summary>
    /// <remarks>
    /// <see cref="ITextExtractor"/> 实现方<b>必须</b>填充本字段为 Markdown；
    /// 即使源文件无结构（例如低质量 OCR 仅产出散段落），也应以扁平 Markdown 段落输出，
    /// 而<b>不能</b>退回 plain text 或在本类上新增并行的纯文本字段——
    /// Markdown 中的标题、表格、列表是后续向量化切块与 LLM 理解的关键语义信号。
    /// </remarks>
    public string Markdown { get; set; } = string.Empty;

    public double Confidence { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    /// <summary>true = OCR (physical scan), false = direct text layer (digital)</summary>
    public bool UsedOcr { get; set; }
}
