using System.Collections.Generic;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// 在 AsyncLocal Scope 内收集本次流水线的所有 AI 调用审计条目。
/// 能力端口返回时从此处读取并合并到 XxxResult.Metadata。
/// </summary>
public interface IAiRunMetadataAccumulator
{
    void Append(AiRunMetadataEntry entry);
    IDictionary<string, object> ToDictionary();
    void Clear();
}
