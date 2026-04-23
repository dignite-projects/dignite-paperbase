using System;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 月度 AI 成本台账。
/// 实现：Dignite.Paperbase.AI（AiCostLedger，用 IDistributedCache 存储）。
/// 读取：核心模块 DocumentClassificationBackgroundJob（预算检查）。
/// </summary>
public interface IAiCostLedger
{
    Task<decimal> GetCurrentMonthUsageAsync(Guid? tenantId);
    Task AddAsync(Guid? tenantId, decimal costUsd);
}
