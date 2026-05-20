using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文档分类完成后发布——下游字段抽取 / 业务消费方按 <see cref="DocumentTypeCode"/> 路由。
/// 薄载荷：正文 Markdown 通过 REST / MCP / 仓储回拉，不随事件投递。
/// <para>
/// 不变契约（issue #188）：所有属性 <c>init</c>-only；<see cref="EventTime"/> 标 <c>required</c>。
/// </para>
/// </summary>
[EventName("Paperbase.Document.Classified")]
public class DocumentClassifiedEto
{
    public string Version { get; init; } = "1.0";

    public Guid DocumentId { get; init; }

    public Guid? TenantId { get; init; }

    /// <summary>
    /// 事件发生时间——Paperbase 在 publish 时填入 <see cref="Volo.Abp.Timing.IClock.Now"/>。
    /// 下游消费方按 <c>(DocumentId, EventType, EventTime)</c> 做幂等（at-least-once 投递）。
    /// </summary>
    public required DateTime EventTime { get; init; }

    public string DocumentTypeCode { get; init; } = default!;

    public double ClassificationConfidence { get; init; }
}
