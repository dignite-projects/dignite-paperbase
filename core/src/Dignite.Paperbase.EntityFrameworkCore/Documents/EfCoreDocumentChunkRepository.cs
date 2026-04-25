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
        var dbContext = await GetDbContextAsync();
        var vector = new Vector(queryVector);

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

        return await query
            .OrderBy(c => c.EmbeddingVector!.CosineDistance(vector))
            .Take(topK)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }
}
