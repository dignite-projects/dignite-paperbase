using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Documents;

public class EfCoreDocumentChunkRepository
    : EfCoreRepository<PaperbaseDbContext, DocumentChunk, Guid>, IDocumentChunkRepository
{
    public EfCoreDocumentChunkRepository(IDbContextProvider<PaperbaseDbContext> dbContextProvider)
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

    public virtual async Task<List<DocumentChunk>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        Guid? documentId = null,
        string? documentTypeCode = null,
        CancellationToken cancellationToken = default)
    {
        var query = await BuildSearchQueryAsync(documentId, documentTypeCode);
        var vector = new Vector(queryVector);

        // EmbeddingVector CLR 类型是 float[]，但 DB 列是 vector(N)。
        // EF.Property<Vector>() 以 Vector 类型访问列，使 pgvector 的 CosineDistance SQL 翻译得以正常工作。
        return await query
            .OrderBy(c => EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(vector))
            .Take(topK)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<DocumentChunkSearchResult>> SearchByVectorWithScoresAsync(
        float[] queryVector,
        int topK,
        Guid? documentId = null,
        string? documentTypeCode = null,
        CancellationToken cancellationToken = default)
    {
        var query = await BuildSearchQueryAsync(documentId, documentTypeCode);
        var vector = new Vector(queryVector);

        var rows = await query
            .OrderBy(c => EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(vector))
            .Select(c => new
            {
                Chunk = c,
                Distance = EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(vector)
            })
            .Take(topK)
            .ToListAsync(GetCancellationToken(cancellationToken));

        return rows
            .Select(r => new DocumentChunkSearchResult(r.Chunk, r.Distance))
            .ToList();
    }

    protected virtual async Task<IQueryable<DocumentChunk>> BuildSearchQueryAsync(
        Guid? documentId,
        string? documentTypeCode)
    {
        var dbContext = await GetDbContextAsync();

        // dbContext.Set<DocumentChunk>() 与 GetDbSetAsync() 一样会应用 ABP 的
        // IMultiTenant / ISoftDelete 全局过滤；下面对 Document 的子查询同理，
        // 两边都会被 CurrentTenant 过滤住，无需在此处再加 TenantId 条件。
        IQueryable<DocumentChunk> query = dbContext.Set<DocumentChunk>().AsNoTracking();

        if (documentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == documentId.Value);
        }
        else if (!string.IsNullOrEmpty(documentTypeCode))
        {
            var matchingDocIds = dbContext.Set<Document>()
                .Where(d => d.DocumentTypeCode == documentTypeCode)
                .Select(d => d.Id);
            query = query.Where(c => matchingDocIds.Contains(c.DocumentId));
        }

        return query;
    }
}
