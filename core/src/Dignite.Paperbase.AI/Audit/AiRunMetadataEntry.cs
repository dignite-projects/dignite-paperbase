using Microsoft.Extensions.AI;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// 单次 AI 调用的审计条目，写入 DocumentPipelineRun.Metadata。
/// 字段约定见 docs/06-模块-Dignite.Paperbase.AI.md §7.3。
/// </summary>
public class AiRunMetadataEntry
{
    public string? ProviderName { get; set; }
    public string? ModelId { get; set; }
    public string? PromptKey { get; set; }
    public string? PromptVersion { get; set; }
    public UsageDetails? Usage { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int LatencyMs { get; set; }
    public double? OutputConfidence { get; set; }
    public string? EvalSampleId { get; set; }
}
