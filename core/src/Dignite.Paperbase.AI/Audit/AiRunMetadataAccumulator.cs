using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Audit;

public class AiRunMetadataAccumulator : IAiRunMetadataAccumulator, ISingletonDependency
{
    private static readonly AsyncLocal<List<AiRunMetadataEntry>?> _entries = new();

    public virtual void Append(AiRunMetadataEntry entry)
    {
        _entries.Value ??= new List<AiRunMetadataEntry>();
        _entries.Value.Add(entry);
    }

    public virtual IDictionary<string, object> ToDictionary()
    {
        var entries = _entries.Value;
        if (entries == null || entries.Count == 0)
            return new Dictionary<string, object>();

        var result = new Dictionary<string, object>();

        // 单次调用：直接展平字段；多次调用：包装为数组
        if (entries.Count == 1)
        {
            FlattenEntry(entries[0], result);
        }
        else
        {
            result["AiCalls"] = entries;
            // 汇总成本和 token
            decimal totalCost = 0;
            long totalTokens = 0;
            foreach (var e in entries)
            {
                totalCost += e.EstimatedCostUsd;
                totalTokens += e.Usage?.TotalTokenCount ?? 0;
            }
            result["TotalEstimatedCostUsd"] = totalCost;
            result["TotalTokens"] = totalTokens;
        }

        return result;
    }

    public virtual void Clear() => _entries.Value = null;

    private static void FlattenEntry(AiRunMetadataEntry entry, IDictionary<string, object> dict)
    {
        if (entry.ProviderName != null) dict["ProviderName"] = entry.ProviderName;
        if (entry.ModelId != null) dict["ModelId"] = entry.ModelId;
        if (entry.PromptKey != null) dict["PromptKey"] = entry.PromptKey;
        if (entry.PromptVersion != null) dict["PromptVersion"] = entry.PromptVersion;
        if (entry.Usage != null)
        {
            dict["Usage.InputTokenCount"] = entry.Usage.InputTokenCount ?? 0;
            dict["Usage.OutputTokenCount"] = entry.Usage.OutputTokenCount ?? 0;
            dict["Usage.TotalTokenCount"] = entry.Usage.TotalTokenCount ?? 0;
        }
        dict["EstimatedCostUsd"] = entry.EstimatedCostUsd;
        dict["LatencyMs"] = entry.LatencyMs;
        if (entry.OutputConfidence.HasValue) dict["OutputConfidence"] = entry.OutputConfidence.Value;
        if (entry.EvalSampleId != null) dict["EvalSampleId"] = entry.EvalSampleId;
    }
}
