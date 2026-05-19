using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreDocumentTypeRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentType, Guid>, IDocumentTypeRepository
{
    public EfCoreDocumentTypeRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<DocumentType>> GetByTenantAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        // 解读 X + 没有继承关系：按 tenantId 精确匹配单层。NULL-safe equality 用分支避免
        // EF Core 对 nullable Guid? == nullable Guid? 翻译歧义。
        var query = tenantId.HasValue
            ? dbSet.Where(t => t.TenantId == tenantId.Value)
            : dbSet.Where(t => t.TenantId == null);

        return await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.TypeCode)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public async Task<DocumentType?> FindByTypeCodeAsync(
        Guid? tenantId,
        string typeCode,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var query = tenantId.HasValue
            ? dbSet.Where(t => t.TenantId == tenantId.Value && t.TypeCode == typeCode)
            : dbSet.Where(t => t.TenantId == null && t.TypeCode == typeCode);

        return await query.FirstOrDefaultAsync(GetCancellationToken(cancellationToken));
    }
}
