using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Domain.Documents;
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

    public override async Task<IQueryable<Document>> WithDetailsAsync()
    {
        return (await GetQueryableAsync())
            .Include(d => d.PipelineRuns);
    }
}
