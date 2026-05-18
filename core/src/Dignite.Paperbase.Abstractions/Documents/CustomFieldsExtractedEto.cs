using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 租户自定义字段（B 机制）抽取完成后发布。
/// 租户字段：租户在 Host 类型 OR 自定义租户类型下定义的字段 schema。
/// 薄载荷：下游通过 REST 回拉具体字段值（含 confidence）。
/// </summary>
[EventName("Paperbase.Document.CustomFieldsExtracted")]
public class CustomFieldsExtractedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? DocumentTypeCode { get; set; }

    /// <summary>
    /// 本次抽取产生的租户字段数量。
    /// </summary>
    public int FieldCount { get; set; }
}
