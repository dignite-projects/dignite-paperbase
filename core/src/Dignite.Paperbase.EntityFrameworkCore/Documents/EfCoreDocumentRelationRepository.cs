using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
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

    public virtual async Task HardDeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        await dbContext.Set<DocumentRelation>()
            .IgnoreQueryFilters()
            .Where(r => r.SourceDocumentId == documentId || r.TargetDocumentId == documentId)
            .ExecuteDeleteAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<Guid>> GetLinkedPeerDocumentIdsAsync(
        Guid documentId,
        Guid? tenantId,
        bool includeDismissed = false,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // R5 [2nd-round review]: tenant predicate applied UNCONDITIONALLY (both branches).
        // Earlier draft applied tenantId only when includeDismissed=true and silently relied on
        // ambient IMultiTenant filter otherwise — an API footgun: a future caller that drops
        // the includeDismissed=true flag or runs without CurrentTenant.Change would silently
        // resolve against host/ambient tenant. Explicit predicate is Hangfire-safe and matches
        // the callsite comments ("tenant flows through Document.TenantId; no ambient dependency").
        // EF Core deduplicates the predicate with the ambient filter — no perf cost.
        IQueryable<DocumentRelation> query = includeDismissed
            ? dbContext.Set<DocumentRelation>().IgnoreQueryFilters()
            : dbContext.Set<DocumentRelation>();

        return await query
            .Where(r => r.TenantId == tenantId)
            .Where(r => r.SourceDocumentId == documentId || r.TargetDocumentId == documentId)
            .Select(r => r.SourceDocumentId == documentId ? r.TargetDocumentId : r.SourceDocumentId)
            .Distinct()
            .ToListAsync(GetCancellationToken(cancellationToken));
    }
}
