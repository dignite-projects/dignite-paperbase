using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 字段抽取流水线完成后发布。统一覆盖 v1 的 <c>MetadataExtractedEto</c>（系统/Host 字段）
/// 和 <c>CustomFieldsExtractedEto</c>（租户字段）—— 字段架构 v2 + 解读 X：
/// 一次 LLM 抽取只跑一层字段定义（按 Document.TenantId 决定 Host vs 租户），单一事件
/// 即可表达"该文档所有可抽取字段已落库"。下游可按 <see cref="TenantId"/> 自行区分场景。
/// 薄载荷：通过 REST / MCP 回拉具体字段值。
/// <para>
/// 不变契约（issue #188）：所有属性 <c>init</c>-only；<see cref="EventTime"/> 标 <c>required</c>。
/// </para>
/// </summary>
[EventName("Paperbase.Document.FieldsExtracted")]
public class FieldsExtractedEto
{
    public string Version { get; init; } = "1.0";

    public Guid DocumentId { get; init; }

    public Guid? TenantId { get; init; }

    /// <summary>
    /// 事件发生时间——Paperbase 在 publish 时填入 <see cref="Volo.Abp.Timing.IClock.Now"/>。
    /// 下游消费方按 <c>(DocumentId, EventType, EventTime)</c> 做幂等（at-least-once 投递）。
    /// </summary>
    public required DateTime EventTime { get; init; }

    public string? DocumentTypeCode { get; init; }

    /// <summary>
    /// 本次抽取产生的非空字段数量（来自该 Document 所属层的 FieldDefinition）。
    /// </summary>
    public int FieldCount { get; init; }
}
