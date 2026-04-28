using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

/// <summary>
/// chunk repository 切到独立 <see cref="PgvectorRagDbContext"/>。
/// Slice C 之前 base class 是 <c>EfCoreRepository&lt;PaperbaseDbContext, ...&gt;</c>，
/// 切换后写入路径走独立 connection string + 独立 EF Core 迁移历史表。
/// </summary>
public class EfCoreDocumentChunkRepository
    : EfCoreRepository<PgvectorRagDbContext, DocumentChunk, Guid>, IDocumentChunkRepository
{
    public EfCoreDocumentChunkRepository(IDbContextProvider<PgvectorRagDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<List<DocumentChunk>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        await dbSet
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(GetCancellationToken(cancellationToken));
    }
}
