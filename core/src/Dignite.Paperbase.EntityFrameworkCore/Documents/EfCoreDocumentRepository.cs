using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreDocumentRepository
    : EfCoreRepository<PaperbaseDbContext, Document, Guid>, IDocumentRepository
{
    public EfCoreDocumentRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<Document?> FindByBlobNameAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .FirstOrDefaultAsync(
                d => d.OriginalFileBlobName == blobName,
                GetCancellationToken(cancellationToken));
    }

    public virtual async Task<Document?> FindByContentHashAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .FirstOrDefaultAsync(
                d => d.FileOrigin.ContentHash == contentHash,
                GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<Document>> GetListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new List<Document>();
        }

        var distinctIds = ids.Distinct().ToList();
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(d => distinctIds.Contains(d.Id))
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public override async Task<IQueryable<Document>> WithDetailsAsync()
    {
        return (await GetQueryableAsync()).IncludeDetails();
    }
}
