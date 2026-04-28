using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

public class EfCoreDocumentVectorRepository
    : EfCoreRepository<PgvectorRagDbContext, DocumentVector, Guid>, IDocumentVectorRepository
{
    public EfCoreDocumentVectorRepository(IDbContextProvider<PgvectorRagDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        await dbSet
            .Where(dv => dv.Id == documentId)
            .ExecuteDeleteAsync(GetCancellationToken(cancellationToken));
    }
}
