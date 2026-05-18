using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 系统通用字段 + Host 字段抽取完成后发布。
/// 系统通用字段：title / summary / topic tags / language 等 pipeline 自动产物。
/// Host 字段：Host 部署者在 DocumentTypeOptions 中注册的类型绑定字段。
/// 薄载荷：下游通过 REST / MCP 回拉详细字段值。
/// </summary>
[EventName("Paperbase.Document.MetadataExtracted")]
public class MetadataExtractedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? DocumentTypeCode { get; set; }

    /// <summary>
    /// 本次抽取产生的字段总数（含系统通用 + Host 字段）。
    /// </summary>
    public int ExtractedFieldCount { get; set; }
}
