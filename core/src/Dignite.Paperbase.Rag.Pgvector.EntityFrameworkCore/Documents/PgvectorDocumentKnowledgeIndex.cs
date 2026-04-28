using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag.Pgvector.Documents;

/// <summary>
/// pgvector-backed implementation of <see cref="IDocumentKnowledgeIndex"/>.
///
/// Slice G adds two capabilities over the former <c>PgvectorDocumentVectorStore</c>:
/// <list type="bullet">
///   <item><description>
///     <see cref="UpsertDocumentAsync"/> — whole-document atomic replace: deletes stale chunks,
///     inserts new ones, then mean-pools the chunk vectors into a <see cref="DocumentVector"/>
///     row — all within the caller's Unit of Work so chunks + document vector commit together.
///   </description></item>
///   <item><description>
///     <see cref="SearchSimilarDocumentsAsync"/> — document-level cosine similarity search
///     over the <see cref="DocumentVector"/> table, replacing the chunk-fetch + mean-pool
///     approach that <c>DocumentRelationInferenceBackgroundJob</c> previously performed.
///   </description></item>
/// </list>
///
/// Supports three search modes:
///   - <see cref="VectorSearchMode.Vector"/>  — pgvector cosine similarity.
///   - <see cref="VectorSearchMode.Keyword"/> — PostgreSQL tsvector full-text (BM25-like).
///   - <see cref="VectorSearchMode.Hybrid"/>  — both paths, fused with Reciprocal Rank Fusion.
///
/// Multi-tenancy: each operation switches the ABP ambient tenant context to match
/// the explicit TenantId carried in the request/record, then also adds an explicit
/// WHERE clause as a second line of defense. Raw-SQL paths (keyword) cannot rely
/// on ABP global filters, so the explicit clause is mandatory.
/// </summary>
[ExposeServices(typeof(IDocumentKnowledgeIndex))]
public class PgvectorDocumentKnowledgeIndex : IDocumentKnowledgeIndex, ITransientDependency
{
    /// <summary>
    /// Per-path recall multiplier in Hybrid mode. Each of vector / keyword recalls
    /// <c>TopK × HybridRecallMultiplier</c> candidates before RRF merging.
    /// </summary>
    protected const int HybridRecallMultiplier = 2;

    private readonly IDbContextProvider<PgvectorRagDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    public PgvectorDocumentKnowledgeIndex(
        IDbContextProvider<PgvectorRagDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
    }

    public virtual DocumentKnowledgeIndexCapabilities Capabilities { get; } = new DocumentKnowledgeIndexCapabilities
    {
        SupportsVectorSearch = true,
        SupportsKeywordSearch = true,
        SupportsHybridSearch = true,
        SupportsStructuredFilter = true,
        SupportsDeleteByDocumentId = true,
        NormalizesScore = true,
        SupportsSearchSimilarDocuments = true
    };

    /// <summary>
    /// Whole-document atomic replace:
    /// <list type="number">
    ///   <item><description>Deletes all existing chunks for the document (ExecuteDeleteAsync — immediate).</description></item>
    ///   <item><description>Inserts the new chunks into the EF change tracker (committed by caller's UoW).</description></item>
    ///   <item><description>Mean-pools the chunk vectors and upserts a <see cref="DocumentVector"/> row (same UoW).</description></item>
    /// </list>
    /// When <see cref="DocumentVectorIndexUpdate.Chunks"/> is empty, all index data is removed
    /// (chunks via ExecuteDeleteAsync, DocumentVector via ExecuteDeleteAsync).
    ///
    /// Recovery path: ExecuteDeleteAsync for chunks is immediate and bypasses UoW; if the process
    /// crashes before the UoW commits, chunks are gone but the DocumentVector is stale. On retry
    /// (Hangfire re-enqueue or reconciliation job), UpsertDocumentAsync is idempotent and self-heals.
    /// </summary>
    public virtual async Task UpsertDocumentAsync(
        DocumentVectorIndexUpdate update,
        CancellationToken cancellationToken = default)
    {
        using var _ = _currentTenant.Change(update.TenantId);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Step 1: Delete existing chunks (immediate — consistent with DeleteByDocumentIdAsync).
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var chunkQuery = dbContext.Set<DocumentChunk>()
                .Where(c => c.DocumentId == update.DocumentId);

            chunkQuery = update.TenantId.HasValue
                ? chunkQuery.Where(c => c.TenantId == update.TenantId.Value)
                : chunkQuery.Where(c => c.TenantId == null);

            await chunkQuery.ExecuteDeleteAsync(cancellationToken);
        }

        if (update.Chunks.Count == 0)
        {
            // No chunks → no embedding → remove stale DocumentVector if present.
            using (_dataFilter.Disable<IMultiTenant>())
            {
                await dbContext.Set<DocumentVector>()
                    .Where(dv => dv.Id == update.DocumentId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            return;
        }

        // Step 2: Insert new chunks (tracked by caller's UoW).
        var chunkDbSet = dbContext.Set<DocumentChunk>();
        foreach (var record in update.Chunks)
        {
            chunkDbSet.Add(new DocumentChunk(
                record.Id,
                record.TenantId,
                record.DocumentId,
                record.ChunkIndex,
                record.Text,
                record.Vector.ToArray(),
                record.DocumentTypeCode,
                record.Title,
                record.PageNumber));
        }

        // Step 3: Mean-pool chunk vectors → document-level embedding.
        var documentEmbedding = MeanPool(update.Chunks.Select(c => c.Vector.ToArray()).ToList());

        // Step 4: Upsert DocumentVector (tracked by caller's UoW).
        DocumentVector? existingDocVector;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            existingDocVector = await dbContext.Set<DocumentVector>()
                .Where(dv => dv.Id == update.DocumentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (existingDocVector != null)
        {
            existingDocVector.Update(
                update.TenantId,
                update.DocumentTypeCode,
                documentEmbedding,
                update.Chunks.Count);
        }
        else
        {
            dbContext.Set<DocumentVector>().Add(new DocumentVector(
                update.DocumentId,
                update.TenantId,
                update.DocumentTypeCode,
                documentEmbedding,
                update.Chunks.Count));
        }
    }

    /// <summary>
    /// Bulk-delete all chunks and the document-level vector for a document.
    /// Executes immediately (bypasses UoW). Tenant scoping is explicit.
    /// </summary>
    public virtual async Task DeleteByDocumentIdAsync(
        Guid documentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var chunkQuery = dbContext.Set<DocumentChunk>()
                .Where(c => c.DocumentId == documentId);

            chunkQuery = tenantId.HasValue
                ? chunkQuery.Where(c => c.TenantId == tenantId.Value)
                : chunkQuery.Where(c => c.TenantId == null);

            await chunkQuery.ExecuteDeleteAsync(cancellationToken);

            // DocumentVector.Id == DocumentId — no tenant predicate needed (global uniqueness).
            await dbContext.Set<DocumentVector>()
                .Where(dv => dv.Id == documentId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Dispatches by <see cref="VectorSearchRequest.Mode"/>. Score normalisation
    /// is mode-specific; the contract is the same across modes: results are ordered
    /// by relevance descending with Score ∈ [0, 1].
    /// </summary>
    public virtual async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        using var _ = _currentTenant.Change(request.TenantId);

        IList<VectorSearchResult> results = request.Mode switch
        {
            VectorSearchMode.Vector  => await SearchVectorAsync(request, request.TopK, cancellationToken),
            VectorSearchMode.Keyword => await SearchKeywordAsync(request, request.TopK, cancellationToken),
            VectorSearchMode.Hybrid  => await SearchHybridAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.Mode, $"Unsupported VectorSearchMode: {request.Mode}")
        };

        if (request.MinScore.HasValue)
            results = results.Where(r => r.Score >= request.MinScore.Value).ToList();

        return (IReadOnlyList<VectorSearchResult>)results;
    }

    /// <summary>
    /// Document-level similarity search over the <see cref="DocumentVector"/> table.
    /// Fetches the source document's mean-pooled vector, then runs cosine distance ordering
    /// against all other documents in the same tenant. Excludes the source document.
    /// Returns empty if the source document has no DocumentVector (not yet embedded).
    /// </summary>
    public virtual async Task<IReadOnlyList<DocumentSimilarityResult>> SearchSimilarDocumentsAsync(
        Guid documentId,
        Guid? tenantId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        using var _ = _currentTenant.Change(tenantId);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        using var filterOff = _dataFilter.Disable<IMultiTenant>();

        // Fetch source document's embedding (select only the vector to avoid loading full entity).
        var sourceEmbedding = await dbContext.Set<DocumentVector>()
            .AsNoTracking()
            .Where(dv => dv.Id == documentId)
            .Select(dv => dv.EmbeddingVector)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceEmbedding == null)
            return [];

        var pgVector = new Vector(sourceEmbedding);

        IQueryable<DocumentVector> query = dbContext.Set<DocumentVector>()
            .AsNoTracking()
            .Where(dv => dv.Id != documentId);

        query = tenantId.HasValue
            ? query.Where(dv => dv.TenantId == tenantId.Value)
            : query.Where(dv => dv.TenantId == null);

        var rows = await query
            .OrderBy(dv => EF.Property<Vector>(dv, nameof(DocumentVector.EmbeddingVector))!.CosineDistance(pgVector))
            .Select(dv => new
            {
                DocumentId = dv.Id,
                dv.DocumentTypeCode,
                Distance = EF.Property<Vector>(dv, nameof(DocumentVector.EmbeddingVector))!.CosineDistance(pgVector)
            })
            .Take(topK)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DocumentSimilarityResult
            {
                DocumentId = r.DocumentId,
                DocumentTypeCode = r.DocumentTypeCode,
                Score = Math.Clamp(1.0 - r.Distance, 0.0, 1.0)
            })
            .ToList();
    }

    /// <summary>
    /// Dense vector path: pgvector cosine distance. Score normalised as <c>1 − distance</c>.
    /// </summary>
    protected virtual async Task<IList<VectorSearchResult>> SearchVectorAsync(
        VectorSearchRequest request,
        int topK,
        CancellationToken cancellationToken)
    {
        if (request.QueryVector.IsEmpty)
            return new List<VectorSearchResult>();

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        IQueryable<DocumentChunk> query = dbContext.Set<DocumentChunk>().AsNoTracking();

        query = request.TenantId.HasValue
            ? query.Where(c => c.TenantId == request.TenantId.Value)
            : query.Where(c => c.TenantId == null);

        if (request.DocumentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == request.DocumentId.Value);
        }
        else if (!string.IsNullOrEmpty(request.DocumentTypeCode))
        {
            query = query.Where(c => c.DocumentTypeCode == request.DocumentTypeCode);
        }

        var pgVector = new Vector(request.QueryVector.ToArray());

        var rows = await query
            .OrderBy(c => EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(pgVector))
            .Select(c => new
            {
                Chunk = c,
                Distance = EF.Property<Vector>(c, nameof(DocumentChunk.EmbeddingVector))!.CosineDistance(pgVector)
            })
            .Take(topK)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new VectorSearchResult
            {
                RecordId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                DocumentTypeCode = r.Chunk.DocumentTypeCode,
                ChunkIndex = r.Chunk.ChunkIndex,
                Text = r.Chunk.ChunkText,
                Score = Math.Clamp(1.0 - r.Distance, 0.0, 1.0),
                Title = r.Chunk.Title,
                PageNumber = r.Chunk.PageNumber
            })
            .ToList();
    }

    /// <summary>
    /// Sparse keyword path: PostgreSQL tsvector full-text via the generated SearchVector column.
    /// Uses plainto_tsquery with the 'simple' regconfig — must match the GENERATED ALWAYS expression.
    /// </summary>
    protected virtual async Task<IList<VectorSearchResult>> SearchKeywordAsync(
        VectorSearchRequest request,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QueryText))
            return new List<VectorSearchResult>();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var chunkTable = GetDelimitedTableName(dbContext, typeof(DocumentChunk));

        var parameters = new List<object>
        {
            request.QueryText,
            (object?)request.TenantId ?? DBNull.Value
        };

        var optionalFilter = string.Empty;
        if (request.DocumentId.HasValue)
        {
            optionalFilter = $@"AND c.""DocumentId"" = {{{parameters.Count}}}";
            parameters.Add(request.DocumentId.Value);
        }
        else if (!string.IsNullOrEmpty(request.DocumentTypeCode))
        {
            optionalFilter = $@"AND c.""DocumentTypeCode"" = {{{parameters.Count}}}";
            parameters.Add(request.DocumentTypeCode);
        }

        parameters.Add(topK);
        var topKIndex = parameters.Count - 1;

        var sql = $@"
            SELECT
                c.""Id""               AS ""Id"",
                c.""DocumentId""       AS ""DocumentId"",
                c.""DocumentTypeCode"" AS ""DocumentTypeCode"",
                c.""ChunkIndex""       AS ""ChunkIndex"",
                c.""ChunkText""        AS ""ChunkText"",
                c.""Title""            AS ""Title"",
                c.""PageNumber""       AS ""PageNumber"",
                ts_rank_cd(c.""SearchVector"", plainto_tsquery('simple', {{0}})) AS ""Rank""
            FROM {chunkTable} c
            WHERE c.""SearchVector"" @@ plainto_tsquery('simple', {{0}})
              AND c.""TenantId"" IS NOT DISTINCT FROM CAST({{1}} AS uuid)
              {optionalFilter}
            ORDER BY ""Rank"" DESC
            LIMIT {{{topKIndex}}}";

        var rows = await dbContext.Database
            .SqlQueryRaw<KeywordSearchRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        return NormalizeMinMax(rows
            .Select(r => new VectorSearchResult
            {
                RecordId = r.Id,
                DocumentId = r.DocumentId,
                DocumentTypeCode = r.DocumentTypeCode,
                ChunkIndex = r.ChunkIndex,
                Text = r.ChunkText,
                Score = r.Rank,
                Title = r.Title,
                PageNumber = r.PageNumber
            })
            .ToList());
    }

    /// <summary>
    /// Hybrid path: dense + sparse, merged via <see cref="RrfFusion"/>.
    /// Each path recalls <see cref="HybridRecallMultiplier"/> × TopK candidates.
    /// Gracefully degrades to single-mode if one query component is missing.
    /// </summary>
    protected virtual async Task<IList<VectorSearchResult>> SearchHybridAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken)
    {
        var recallTopK = request.TopK * HybridRecallMultiplier;

        var vectorResults = !request.QueryVector.IsEmpty
            ? await SearchVectorAsync(request, recallTopK, cancellationToken)
            : new List<VectorSearchResult>();

        var keywordResults = !string.IsNullOrWhiteSpace(request.QueryText)
            ? await SearchKeywordAsync(request, recallTopK, cancellationToken)
            : new List<VectorSearchResult>();

        return RrfFusion.Merge(
            (IReadOnlyList<VectorSearchResult>)vectorResults,
            (IReadOnlyList<VectorSearchResult>)keywordResults,
            request.TopK);
    }

    /// <summary>Min-max normalize Score within the supplied list. Used by the keyword path.</summary>
    protected virtual IList<VectorSearchResult> NormalizeMinMax(IList<VectorSearchResult> results)
    {
        if (results.Count == 0)
            return results;

        var max = results.Max(r => r.Score) ?? 0;
        var min = results.Min(r => r.Score) ?? 0;
        var range = max - min;

        return results
            .Select(r => new VectorSearchResult
            {
                RecordId = r.RecordId,
                DocumentId = r.DocumentId,
                DocumentTypeCode = r.DocumentTypeCode,
                ChunkIndex = r.ChunkIndex,
                Text = r.Text,
                Score = range > 0 ? ((r.Score ?? 0) - min) / range : 1.0,
                Title = r.Title,
                PageNumber = r.PageNumber
            })
            .ToList();
    }

    /// <summary>
    /// Mean-pool a list of vectors into a single document-level vector.
    /// All input vectors must share the same dimension.
    /// </summary>
    private static float[] MeanPool(IReadOnlyList<float[]> vectors)
    {
        var dim = vectors[0].Length;
        var result = new float[dim];
        foreach (var v in vectors)
            for (var i = 0; i < dim; i++)
                result[i] += v[i];
        for (var i = 0; i < dim; i++)
            result[i] /= vectors.Count;
        return result;
    }

    protected virtual string GetDelimitedTableName(PgvectorRagDbContext dbContext, Type entityClrType)
    {
        var entityType = dbContext.Model.FindEntityType(entityClrType)
            ?? throw new InvalidOperationException($"Entity type {entityClrType.Name} is not part of the PgvectorRag EF Core model.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {entityClrType.Name} is not mapped to a table.");

        var sqlGenerationHelper = dbContext.GetService<ISqlGenerationHelper>();
        return sqlGenerationHelper.DelimitIdentifier(tableName, entityType.GetSchema());
    }

    private sealed class KeywordSearchRow
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public string? DocumentTypeCode { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkText { get; set; } = default!;
        public string? Title { get; set; }
        public int? PageNumber { get; set; }
        public double Rank { get; set; }
    }
}
