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
/// pgvector-backed implementation of <see cref="IDocumentVectorStore"/>.
/// Slice C 起改用独立的 <see cref="PgvectorRagDbContext"/>——在 Slice B 完成 chunk
/// 反范式化（去 JOIN <c>Documents</c> 表）之后这次切换是干净的，没有任何 fallback 路径。
///
/// <para>
/// 类名暂保留 <c>PgvectorDocumentVectorStore</c>；Slice F/G 接口改名为 <c>IDocumentKnowledgeIndex</c>
/// 并引入 <c>UpsertDocumentAsync</c> 时一并改为 <c>PgvectorDocumentKnowledgeIndex</c>。
/// </para>
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

    private readonly IDbContextProvider<PgvectorRagDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    public PgvectorDocumentVectorStore(
        IDbContextProvider<PgvectorRagDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
    }

    public virtual VectorStoreCapabilities Capabilities { get; } = new VectorStoreCapabilities
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
                    existing.UpdateRecord(
                        record.TenantId,
                        record.DocumentId,
                        record.ChunkIndex,
                        record.Text,
                        record.Vector.ToArray(),
                        record.DocumentTypeCode,
                        record.Title,
                        record.PageNumber);
                }
                else
                {
                    dbSet.Add(new DocumentChunk(
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
            }
        }
    }

    /// <summary>
    /// Bulk-delete all chunks for a document. Executes immediately (bypasses UoW),
    /// consistent with <see cref="EfCoreDocumentChunkRepository.DeleteByDocumentIdAsync"/>.
    ///
    /// Tenant scoping is explicit: the provider disables ABP's ambient multi-tenant
    /// filter for this operation and adds its own TenantId predicate, so background
    /// jobs and CLI callers cannot accidentally delete in the wrong ambient tenant.
    /// </summary>
    public virtual async Task DeleteByDocumentIdAsync(
        Guid documentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var query = dbContext.Set<DocumentChunk>()
                .Where(c => c.DocumentId == documentId);

            query = tenantId.HasValue
                ? query.Where(c => c.TenantId == tenantId.Value)
                : query.Where(c => c.TenantId == null);

            await query.ExecuteDeleteAsync(cancellationToken);
        }
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
            // 反范式化（Slice B）后直接用 chunk 行上的 DocumentTypeCode 过滤，
            // 不再 JOIN PaperbaseDocuments；这是 Slice C 切独立 PgvectorRagDbContext 的前置。
            query = query.Where(c => c.DocumentTypeCode == request.DocumentTypeCode);
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
                DocumentTypeCode = r.Chunk.DocumentTypeCode,
                ChunkIndex = r.Chunk.ChunkIndex,
                Text = r.Chunk.ChunkText,
                // Keep legacy cosine similarity semantics while enforcing the
                // provider contract: Score must stay in [0, 1].
                Score = Math.Clamp(1.0 - r.Distance, 0.0, 1.0),
                Title = r.Chunk.Title,
                PageNumber = r.Chunk.PageNumber
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
        var chunkTable = GetDelimitedTableName(dbContext, typeof(DocumentChunk));

        // Build the SQL with positional parameters. Filter precedence matches
        // SearchVectorAsync: DocumentId wins over DocumentTypeCode, both optional.
        // Raw SQL is required because the SearchVector column is intentionally
        // hidden from the EF model (managed entirely by the GENERATED ALWAYS
        // constraint in PostgreSQL); LINQ translation has no shadow property
        // for it.
        //
        // Slice B: keyword 路径同步去 JOIN PaperbaseDocuments——直接读 chunk 行上的反范式
        // DocumentTypeCode / Title / PageNumber，为 Slice C 切独立 PgvectorRagDbContext 做物理前置。
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

        // IS NOT DISTINCT FROM treats NULL = NULL, so the host-tenant case
        // (TenantId = null) and explicit-tenant case share the same clause.
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

    protected virtual string GetDelimitedTableName(PgvectorRagDbContext dbContext, Type entityClrType)
    {
        var entityType = dbContext.Model.FindEntityType(entityClrType)
            ?? throw new InvalidOperationException($"Entity type {entityClrType.Name} is not part of the PgvectorRag EF Core model.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {entityClrType.Name} is not mapped to a table.");

        var sqlGenerationHelper = dbContext.GetService<ISqlGenerationHelper>();
        return sqlGenerationHelper.DelimitIdentifier(tableName, entityType.GetSchema());
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
        public string? DocumentTypeCode { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkText { get; set; } = default!;
        public string? Title { get; set; }
        public int? PageNumber { get; set; }
        public double Rank { get; set; }
    }
}
