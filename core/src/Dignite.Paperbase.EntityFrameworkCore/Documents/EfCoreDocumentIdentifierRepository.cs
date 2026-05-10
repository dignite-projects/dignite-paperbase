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

/// <summary>
/// Issue #115 L1: <see cref="IDocumentIdentifierRepository"/> 的 EF Core 实现。
/// 多租户由 ABP <c>IMultiTenant</c> 自动谓词过滤；显式查询不再重复加 TenantId 谓词，
/// 但对 <see cref="RemoveByDocumentIdAsync"/> 这种批量删除走 ExecuteDeleteAsync 的路径，
/// 由 ABP query filter 注入 TenantId 谓词保证多租户隔离。
/// </summary>
public class EfCoreDocumentIdentifierRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentIdentifier, Guid>, IDocumentIdentifierRepository
{
    public EfCoreDocumentIdentifierRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<List<Guid>> FindDocumentIdsAsync(
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(i => i.IdentifierType == identifierType && i.IdentifierValue == identifierValue)
            .Select(i => i.DocumentId)
            .Distinct()
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<DocumentIdentifier>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(i => i.DocumentId == documentId)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<bool> ExistsAsync(
        Guid documentId,
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .AnyAsync(
                i => i.DocumentId == documentId
                     && i.IdentifierType == identifierType
                     && i.IdentifierValue == identifierValue,
                GetCancellationToken(cancellationToken));
    }

    public virtual async Task RemoveByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        await dbSet
            .Where(i => i.DocumentId == documentId)
            .ExecuteDeleteAsync(GetCancellationToken(cancellationToken));
    }
}
