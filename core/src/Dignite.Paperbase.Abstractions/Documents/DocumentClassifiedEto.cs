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
    /// 文档提取的结构化 Markdown，随事件携带，省去业务模块回查核心仓储。
    /// 业务模块可直接喂给 LLM（结构信号有助于字段抽取）；需要纯文本时用 <see cref="Dignite.Paperbase.Documents.MarkdownStripper"/>。
    /// </summary>
    public string? Markdown { get; set; }
}
