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
[ExposeServices(typeof(IDocumentVectorStore))]
public class PgvectorDocumentVectorStore : IDocumentVectorStore, ITransientDependency
{
    /// <summary>
    /// Per-path recall multiplier in Hybrid mode. Each of vector / keyword recalls
    /// <c>TopK × HybridRecallMultiplier</c> candidates before RRF merging. Larger
    /// values trade query cost for better fusion quality; 2× is a reasonable
    /// starting point and matches common RRF tuning advice.
    /// </summary>
    protected const int HybridRecallMultiplier = 2;

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
        SupportsKeywordSearch = true,
        SupportsHybridSearch = true,
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
    /// Dispatches by <see cref="VectorSearchRequest.Mode"/>. Score normalisation
    /// is mode-specific; the contract for every mode is the same: results are
    /// ordered by relevance descending with <see cref="VectorSearchResult.Score"/> ∈ [0, 1].
    /// <see cref="VectorSearchRequest.MinScore"/> is applied uniformly after the
    /// mode-specific search, so the same threshold can be reused across modes
    /// (note that the <em>meaning</em> of a 0.65 threshold differs between
    /// cosine similarity, keyword rank, and RRF — tune per mode if needed).
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
    /// Dense vector path: pgvector cosine distance. Score is normalized as
    /// <c>1 − distance</c> (higher = more relevant). Tenant filter is enforced
    /// both via ABP ambient context (set by caller) and an explicit WHERE clause
    /// — see class-level multi-tenancy comment.
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

        // Explicit TenantId WHERE clause — required because SearchAsync may be called from
        // Hangfire background jobs where ABP's ambient ICurrentTenant is not set by the
        // HTTP pipeline. ICurrentTenant.Change() above aligns the ABP global filter, and
        // this clause is a second line of defense against cross-tenant data leakage.
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
            .Take(topK)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new VectorSearchResult
            {
                RecordId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                // DocumentTypeCode is not stored on DocumentChunk; null until a follow-up
                // slice wires the join in the projection.
                DocumentTypeCode = null,
                ChunkIndex = r.Chunk.ChunkIndex,
                Text = r.Chunk.ChunkText,
                // Normalize: cosine distance ∈ [0, 1] for unit vectors → similarity ∈ [0, 1]
                Score = 1.0 - r.Distance,
                Title = null,
                PageNumber = null
            })
            .ToList();
    }

    /// <summary>
    /// Sparse keyword path: PostgreSQL <c>tsvector</c> full-text via the generated
    /// <c>SearchVector</c> column added in the Slice 7 migration. Uses
    /// <c>plainto_tsquery</c> with the <c>simple</c> regconfig to preserve IDs,
    /// numbers, names, and Japanese tokens (which have no built-in PG stemmer).
    ///
    /// Regconfig symmetry: the <c>'simple'</c> literal here MUST match the regconfig
    /// used by the GENERATED ALWAYS expression on the SearchVector column (see
    /// migration 20260428004038_Slice7_AddDocumentChunkSearchVector). A mismatch
    /// silently disables the GIN index and falls back to sequential scan.
    ///
    /// Score is min-max normalized to [0, 1] within the result set: ts_rank_cd is
    /// unbounded and corpus-dependent, so per-query normalisation is the only
    /// portable way to keep the range.
    /// </summary>
    protected virtual async Task<IList<VectorSearchResult>> SearchKeywordAsync(
        VectorSearchRequest request,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QueryText))
            return new List<VectorSearchResult>();

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Build the SQL with positional parameters. Filter precedence matches
        // SearchVectorAsync: DocumentId wins over DocumentTypeCode, both optional.
        // Raw SQL is required because the SearchVector column is intentionally
        // hidden from the EF model (managed entirely by the GENERATED ALWAYS
        // constraint in PostgreSQL); LINQ translation has no shadow property
        // for it.
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
            optionalFilter = $@"AND d.""DocumentTypeCode"" = {{{parameters.Count}}}";
            parameters.Add(request.DocumentTypeCode);
        }

        parameters.Add(topK);
        var topKIndex = parameters.Count - 1;

        // IS NOT DISTINCT FROM treats NULL = NULL, so the host-tenant case
        // (TenantId = null) and explicit-tenant case share the same clause.
        var sql = $@"
            SELECT
                c.""Id""           AS ""Id"",
                c.""DocumentId""   AS ""DocumentId"",
                c.""ChunkIndex""   AS ""ChunkIndex"",
                c.""ChunkText""    AS ""ChunkText"",
                ts_rank_cd(c.""SearchVector"", plainto_tsquery('simple', {{0}})) AS ""Rank""
            FROM ""PaperbaseDocumentChunks"" c
            INNER JOIN ""PaperbaseDocuments"" d ON d.""Id"" = c.""DocumentId""
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
                DocumentTypeCode = null,
                ChunkIndex = r.ChunkIndex,
                Text = r.ChunkText,
                Score = r.Rank,
                Title = null,
                PageNumber = null
            })
            .ToList());
    }

    /// <summary>
    /// Hybrid path: dense + sparse, merged via <see cref="RrfFusion"/>. Each path
    /// recalls <see cref="HybridRecallMultiplier"/> × TopK candidates so the
    /// fusion has enough headroom; RRF then trims back to TopK.
    ///
    /// Graceful degradation: a request without QueryVector falls back to
    /// keyword-only; without QueryText falls back to vector-only. Returning
    /// results from a single path still satisfies "Hybrid mode requested" —
    /// the caller asked for the best available, not for the strict union.
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

        // RrfFusion handles min-max normalization of the merged scores so the
        // [0, 1] contract holds for hybrid output as well as single-mode output.
        return RrfFusion.Merge(
            (IReadOnlyList<VectorSearchResult>)vectorResults,
            (IReadOnlyList<VectorSearchResult>)keywordResults,
            request.TopK);
    }

    /// <summary>
    /// Min-max normalize Score within the supplied list. Used by the keyword
    /// path because ts_rank_cd has no fixed range. Empty / single-item lists
    /// are normalized to 1.0 (degenerate case has no meaningful spread).
    /// </summary>
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
    /// Internal projection type for the keyword-path raw SQL query. Property
    /// names must match the SELECT aliases exactly (EF Core SqlQueryRaw maps
    /// by name).
    /// </summary>
    private sealed class KeywordSearchRow
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkText { get; set; } = default!;
        public double Rank { get; set; }
    }
}
