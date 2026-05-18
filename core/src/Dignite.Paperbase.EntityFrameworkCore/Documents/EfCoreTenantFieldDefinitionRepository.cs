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

public class EfCoreTenantFieldDefinitionRepository
    : EfCoreRepository<PaperbaseDbContext, TenantFieldDefinition, Guid>, ITenantFieldDefinitionRepository
{
    public EfCoreTenantFieldDefinitionRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<TenantFieldDefinition>> GetByDocumentTypeAsync(
        Guid? tenantId,
        string documentTypeCode,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(f => f.TenantId == tenantId && f.DocumentTypeCode == documentTypeCode)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public async Task<TenantFieldDefinition?> FindByNameAsync(
        Guid? tenantId,
        string documentTypeCode,
        string name,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(
            f => f.TenantId == tenantId
              && f.DocumentTypeCode == documentTypeCode
              && f.Name == name,
            GetCancellationToken(cancellationToken));
    }
}
