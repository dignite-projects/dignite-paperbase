using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Audit;

public class AiCostLedger : IAiCostLedger, IScopedDependency
{
    private readonly IDistributedCache<string> _cache;

    public AiCostLedger(IDistributedCache<string> cache)
    {
        _cache = cache;
    }

    public virtual async Task<decimal> GetCurrentMonthUsageAsync(Guid? tenantId)
    {
        var key = BuildKey(tenantId);
        var raw = await _cache.GetOrAddAsync(key, () => Task.FromResult("0"), () => GetCacheOptions());
        return decimal.TryParse(raw, out var v) ? v : 0m;
    }

    public virtual async Task AddAsync(Guid? tenantId, decimal costUsd)
    {
        if (costUsd <= 0m) return;

        var key = BuildKey(tenantId);
        var current = await GetCurrentMonthUsageAsync(tenantId);
        await _cache.SetAsync(key, (current + costUsd).ToString("G"), GetCacheOptions());
    }

    private static string BuildKey(Guid? tenantId)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        return $"AiCost:{tenantId?.ToString() ?? "host"}:{month}";
    }

    private static DistributedCacheEntryOptions GetCacheOptions()
    {
        var now = DateTime.UtcNow;
        var expiry = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1).AddDays(1);
        return new DistributedCacheEntryOptions { AbsoluteExpiration = expiry };
    }
}
