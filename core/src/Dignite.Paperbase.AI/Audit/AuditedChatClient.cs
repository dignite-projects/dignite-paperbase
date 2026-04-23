using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.AI.Audit;

/// <summary>
/// IChatClient 审计封装。
/// 每次调用结束后把 Provider / Model / Usage / Cost / Latency 写入 IAiRunMetadataAccumulator
/// 并更新 IAiCostLedger 月度计数。
/// 不做预算检查——预算由核心模块 BackgroundJob 在调用前负责。
/// </summary>
public class AuditedChatClient : ITransientDependency
{
    private readonly IChatClient _inner;
    private readonly IAiRunMetadataAccumulator _accumulator;
    private readonly IAiCostEstimator _costEstimator;
    private readonly IAmbientAiCallContext _callContext;
    private readonly IAiCostLedger _costLedger;
    private readonly ICurrentTenant _currentTenant;

    public AuditedChatClient(
        IChatClient inner,
        IAiRunMetadataAccumulator accumulator,
        IAiCostEstimator costEstimator,
        IAmbientAiCallContext callContext,
        IAiCostLedger costLedger,
        ICurrentTenant currentTenant)
    {
        _inner = inner;
        _accumulator = accumulator;
        _costEstimator = costEstimator;
        _callContext = callContext;
        _costLedger = costLedger;
        _currentTenant = currentTenant;
    }

    public virtual async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var response = await _inner.GetResponseAsync(messages, options, cancellationToken);
        sw.Stop();

        var usage = response.Usage;
        var meta = _inner.GetService<ChatClientMetadata>();
        var providerName = meta?.ProviderName;
        var modelId = response.ModelId ?? meta?.DefaultModelId;
        var costUsd = _costEstimator.Estimate(providerName, modelId, usage);

        // 异步记账，失败不阻断主流程
        _ = _costLedger.AddAsync(_currentTenant.Id, costUsd);

        _accumulator.Append(new AiRunMetadataEntry
        {
            ProviderName = providerName,
            ModelId = modelId,
            PromptKey = _callContext.CurrentPromptKey,
            PromptVersion = _callContext.CurrentPromptVersion,
            Usage = usage,
            EstimatedCostUsd = costUsd,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            OutputConfidence = _callContext.OutputConfidence,
            EvalSampleId = _callContext.EvalSampleId,
        });

        return response;
    }
}
