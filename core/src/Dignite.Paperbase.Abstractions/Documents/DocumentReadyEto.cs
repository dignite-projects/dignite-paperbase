using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 全流水线完成 + 通过 OCR 置信度门槛后发布——下游消费方的"可信信号"。
/// 这是受置信度门槛约束的**唯一**生命周期事件：
/// <list type="bullet">
///   <item>OCR confidence ≥ 门槛 → 自动发布</item>
///   <item>OCR confidence &lt; 门槛 → 文档进待人工审核队列；操作员通过后才发布</item>
/// </list>
/// 大多数下游业务消费方应订阅此事件而非早期阶段事件（DocumentUploaded/OCRCompleted/...）。
/// </summary>
[EventName("Paperbase.Document.Ready")]
public class DocumentReadyEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? DocumentTypeCode { get; set; }

    /// <summary>
    /// 最终 OCR 置信度（数字版抽取通常为 1.0）。
    /// </summary>
    public double OcrConfidence { get; set; }
}
