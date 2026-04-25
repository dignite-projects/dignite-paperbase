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

    public virtual async Task<List<DocumentRelation>> GetListByDocumentIdsAsync(
        IReadOnlyCollection<Guid> documentIds,
        bool includeAiSuggested = true,
        CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
        {
            return new List<DocumentRelation>();
        }

        var distinctDocumentIds = documentIds.Distinct().ToList();
        var dbSet = await GetDbSetAsync();
        var sourceQuery = dbSet.Where(r => distinctDocumentIds.Contains(r.SourceDocumentId));
        var targetQuery = dbSet.Where(r => distinctDocumentIds.Contains(r.TargetDocumentId));

        if (!includeAiSuggested)
        {
            sourceQuery = sourceQuery.Where(r => r.Source != RelationSource.AiSuggested);
            targetQuery = targetQuery.Where(r => r.Source != RelationSource.AiSuggested);
        }

        var sourceRelations = await sourceQuery.ToListAsync(GetCancellationToken(cancellationToken));
        var targetRelations = await targetQuery.ToListAsync(GetCancellationToken(cancellationToken));

        return sourceRelations
            .Concat(targetRelations)
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .ToList();
    }
}
