using System;
using Volo.Abp;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 当租户当月 AI 成本超过预算时抛出。
/// 由核心模块 DocumentClassificationBackgroundJob 在调用前检查后抛出或捕获。
/// </summary>
public class AiBudgetExceededException : BusinessException
{
    public Guid? TenantId { get; }
    public decimal BudgetUsd { get; }
    public decimal UsedUsd { get; }

    public AiBudgetExceededException(Guid? tenantId, decimal budgetUsd, decimal usedUsd)
        : base("Paperbase:AiBudgetExceeded",
               $"Monthly AI budget exceeded. Budget: ${budgetUsd:F2}, Used: ${usedUsd:F2}")
    {
        TenantId = tenantId;
        BudgetUsd = budgetUsd;
        UsedUsd = usedUsd;
    }
}
