using Microsoft.Extensions.AI;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// 按 Provider + Model + Usage 估算单次 AI 调用的美元成本。
/// </summary>
public interface IAiCostEstimator
{
    decimal Estimate(string? providerName, string? modelId, UsageDetails? usage);
}
