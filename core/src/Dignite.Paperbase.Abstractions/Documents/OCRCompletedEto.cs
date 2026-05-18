using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文本提取（OCR 或数字版抽取）完成后发布。
/// 携带 OCR 置信度供下游决策；不受置信度门槛约束（仅 DocumentReadyEto 受约束）。
/// </summary>
[EventName("Paperbase.Document.OCRCompleted")]
public class OCRCompletedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    /// <summary>
    /// OCR 置信度（0.0 - 1.0）。仅 OCR 路径有意义；数字版抽取此值通常 = 1.0。
    /// </summary>
    public double OcrConfidence { get; set; }

    /// <summary>
    /// 是否实际走了 OCR 路径（true = 图像 OCR；false = 数字版直接抽取）。
    /// </summary>
    public bool UsedOcr { get; set; }
}
