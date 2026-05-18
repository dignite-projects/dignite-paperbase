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

public class EfCoreDocumentTenantFieldRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentTenantField, Guid>, IDocumentTenantFieldRepository
{
    public EfCoreDocumentTenantFieldRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<DocumentTenantField>> GetByDocumentAsync(
        Guid? tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(f => f.TenantId == tenantId && f.DocumentId == documentId)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public async Task<DocumentTenantField?> FindByDocumentAndNameAsync(
        Guid? tenantId,
        Guid documentId,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(
            f => f.TenantId == tenantId && f.DocumentId == documentId && f.FieldName == fieldName,
            GetCancellationToken(cancellationToken));
    }
}
