using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreDocumentRelationRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentRelation, Guid>, IDocumentRelationRepository
{
    public EfCoreDocumentRelationRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<List<DocumentRelation>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(r => r.SourceDocumentId == documentId || r.TargetDocumentId == documentId)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }
}
