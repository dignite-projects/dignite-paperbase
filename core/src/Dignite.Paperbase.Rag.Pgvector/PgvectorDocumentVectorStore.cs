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
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag.Pgvector;

/// <summary>
/// pgvector-backed implementation of <see cref="IDocumentVectorStore"/>.
/// Bridges the Rag abstraction to the existing <see cref="PaperbaseDbContext"/> /
/// <see cref="DocumentChunk"/> infrastructure.
///
/// Multi-tenancy: each operation switches the ABP ambient tenant context to match
/// the explicit TenantId carried in the request/record, then also adds an explicit
/// WHERE clause as a second line of defense.
/// </summary>
[ExposeServices(typeof(IDocumentVectorStore))]
public class PgvectorDocumentVectorStore : IDocumentVectorStore, ITransientDependency
{
    private readonly IDbContextProvider<PaperbaseDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;

    public PgvectorDocumentVectorStore(
        IDbContextProvider<PaperbaseDbContext> dbContextProvider,
        ICurrentTenant currentTenant)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
    }

    public VectorStoreCapabilities Capabilities { get; } = new VectorStoreCapabilities
    {
        SupportsVectorSearch = true,
        SupportsKeywordSearch = false,
        SupportsHybridSearch = false,
        SupportsStructuredFilter = true,
        SupportsDeleteByDocumentId = true,
        NormalizesScore = true
    };

    /// <summary>
    /// Insert or update chunk records. Groups by TenantId to minimise context switches.
    /// Changes are committed by the caller's Unit of Work — do not call SaveChanges here.
    /// </summary>
    public virtual async Task UpsertAsync(
        IReadOnlyList<DocumentVectorRecord> records,
        CancellationToken cancellationToken = default)
    {
        var groups = records.GroupBy(r => r.TenantId);
        foreach (var group in groups)
        {
            using var _ = _currentTenant.Change(group.Key);
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var dbSet = dbContext.Set<DocumentChunk>();

            var ids = group.Select(r => r.Id).ToList();
            var existingById = await dbSet
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

            foreach (var record in group)
            {
                if (existingById.TryGetValue(record.Id, out var existing))
                {
                    existing.UpdateEmbedding(record.Vector.ToArray());
                }
                else
                {
                    dbSet.Add(new DocumentChunk(
                        record.Id,
                        record.TenantId,
                        record.DocumentId,
                        record.ChunkIndex,
                        record.Text,
                        record.Vector.ToArray()));
                }
            }
        }
    }

    /// <summary>
    /// Bulk-delete all chunks for a document. Executes immediately (bypasses UoW),
    /// consistent with <see cref="Documents.EfCoreDocumentChunkRepository.DeleteByDocumentIdAsync"/>.
    /// Tenant isolation is guaranteed by ABP's global IMultiTenant query filter.
    /// </summary>
    public virtual async Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Set<DocumentChunk>()
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Vector similarity search. Score is normalized to [0, 1] via
    /// <c>Score = 1.0 − cosineDistance</c> (higher = more relevant).
    /// </summary>
    public virtual async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        using var _ = _currentTenant.Change(request.TenantId);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        IQueryable<DocumentChunk> query = dbContext.Set<DocumentChunk>().AsNoTracking();

        // Explicit TenantId WHERE clause — required because SearchAsync may be called from
        // Hangfire background jobs where ABP's ambient ICurrentTenant is not set by the
        // HTTP pipeline. ICurrentTenant.Change() above aligns the ABP global filter, and
        // this clause is a second line of defense against cross-tenant data leakage.
        // (EfCoreDocumentChunkRepository omits this because it is always called from
        // within an already-scoped HTTP request context.)
        query = request.TenantId.HasValue
            ? query.Where(c => c.TenantId == request.TenantId.Value)
            : query.Where(c => c.TenantId == null);

        if (request.DocumentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == request.DocumentId.Value);
        }
        else if (!string.IsNullOrEmpty(request.DocumentTypeCode))
        {
            var matchingDocIds = dbContext.Set<Document>()
                .Where(d => d.DocumentTypeCode == request.DocumentTypeCode)
                .Select(d => d.Id);
            query = query.Where(c => matchingDocIds.Contains(c.DocumentId));
        }

        var pgVector = new Vector(request.QueryVector.ToArray());

        // EmbeddingVector CLR type is float[], but the DB column is vector(N).
        // EF.Property<Vector>() accesses the column as Vector so pgvector's
        // CosineDistance SQL translation works correctly.
        var rows = await query
            .OrderBy(c => EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(pgVector))
            .Select(c => new
            {
                Chunk = c,
                Distance = EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(pgVector)
            })
            .Take(request.TopK)
            .ToListAsync(cancellationToken);

        var results = rows
            .Select(r => new VectorSearchResult
            {
                RecordId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                // DocumentTypeCode is not stored on DocumentChunk; null until Slice 5 wires in the join.
                DocumentTypeCode = null,
                ChunkIndex = r.Chunk.ChunkIndex,
                Text = r.Chunk.ChunkText,
                // Normalize: cosine distance ∈ [0, 1] for unit vectors → similarity ∈ [0, 1]
                Score = 1.0 - r.Distance,
                Title = null,
                PageNumber = null
            })
            .ToList();

        if (request.MinScore.HasValue)
            results = results.Where(r => r.Score >= request.MinScore.Value).ToList();

        return results;
    }
}
