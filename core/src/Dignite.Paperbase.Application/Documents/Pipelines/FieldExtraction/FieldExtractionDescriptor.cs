using Dignite.Paperbase.Documents;

namespace Dignite.Paperbase.Documents.Pipelines.FieldExtraction;

/// <summary>
/// Workflow 内部 DTO：字段抽取的运行时描述（与持久化层 <see cref="FieldDefinition"/> 解耦）。
/// 字段架构 v2 + 解读 X：一次 LLM 调用只跑一层字段定义（按 Document.TenantId 决定 Host vs 租户），
/// 所以 descriptor 不需要携带来源标记——descriptor 列表本身就是单层 schema。
/// </summary>
public sealed record FieldExtractionDescriptor(
    string Name,
    string Prompt,
    FieldDataType DataType,
    bool IsRequired);
