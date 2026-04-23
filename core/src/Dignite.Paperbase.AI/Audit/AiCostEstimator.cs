using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// 初版硬编码单价表。后续可改为从配置读取。
/// 单价单位：USD per 1K tokens。
/// </summary>
public class AiCostEstimator : IAiCostEstimator, ITransientDependency
{
    public virtual decimal Estimate(string? providerName, string? modelId, UsageDetails? usage)
    {
        if (usage == null) return 0m;

        var inputTokens = usage.InputTokenCount ?? 0;
        var outputTokens = usage.OutputTokenCount ?? 0;

        // 单价表（USD / 1K tokens）
        var (inputPrice, outputPrice) = modelId?.ToLowerInvariant() switch
        {
            "gpt-4o" => (0.005m, 0.015m),
            "gpt-4o-mini" => (0.00015m, 0.0006m),
            "gpt-4-turbo" => (0.01m, 0.03m),
            "gpt-3.5-turbo" => (0.0005m, 0.0015m),
            _ => (0.001m, 0.002m)  // 保守默认值
        };

        return (inputTokens * inputPrice + outputTokens * outputPrice) / 1000m;
    }
}
