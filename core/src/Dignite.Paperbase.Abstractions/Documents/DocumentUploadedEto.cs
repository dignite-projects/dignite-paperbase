using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文档上传完成（落库 + Blob 落盘）后发布；处于通道流水线起点。
/// 薄载荷：下游通过 REST / MCP 回拉详细数据。不受置信度门槛约束。
/// </summary>
[EventName("Paperbase.Document.Uploaded")]
public class DocumentUploadedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? FileName { get; set; }

    public long FileSize { get; set; }

    public string? ContentType { get; set; }
}
