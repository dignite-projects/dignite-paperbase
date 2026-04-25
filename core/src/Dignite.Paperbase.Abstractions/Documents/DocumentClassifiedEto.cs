using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

[EventName("Dignite.Paperbase.DocumentClassified")]
public class DocumentClassifiedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string DocumentTypeCode { get; set; } = default!;

    public double ClassificationConfidence { get; set; }

    /// <summary>
    /// 文档提取的全文，随事件携带，省去业务模块回查核心仓储。
    /// </summary>
    public string? ExtractedText { get; set; }
}
