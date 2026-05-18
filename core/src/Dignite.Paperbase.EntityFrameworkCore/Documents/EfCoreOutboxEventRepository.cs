using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreOutboxEventRepository
    : EfCoreRepository<PaperbaseDbContext, OutboxEvent, Guid>, IOutboxEventRepository
{
    public EfCoreOutboxEventRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<OutboxEvent?> FindByKeyAsync(
        Guid? tenantId,
        Guid documentId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        // 显式 TenantId 谓词，不依赖 ambient DataFilter — 见 doc-chat-anti-patterns.md 反例 B。
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(
            e => e.TenantId == tenantId
              && e.DocumentId == documentId
              && e.EventType == eventType,
            GetCancellationToken(cancellationToken));
    }
}
