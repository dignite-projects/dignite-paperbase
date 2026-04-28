using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.EntityFrameworkCore;
using Dignite.Paperbase.Rag;
using Dignite.Paperbase.Rag.Pgvector;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Production hybrid-search benchmark. Runs against a real PostgreSQL instance with
/// pgvector + tsvector (all migrations applied). Validates that hybrid retrieval
/// outperforms pure vector retrieval on a desensitized real corpus.
///
/// <b>Run:</b>
/// <code>
///   PAPERBASE_BENCH_PGCONN="Host=...;Database=...;Username=...;Password=..."
///   dotnet test core/ --filter "Category=Production"
/// </code>
///
/// The test is skipped when <c>PAPERBASE_BENCH_PGCONN</c> or the production dataset
/// is not available, so it never interferes with the regular CI test suite.
///
/// See <c>docs/benchmarks/README.md</c> for dataset preparation instructions.
/// </summary>
[Trait("Category", "Production")]
public class ProductionHybridSearchBenchmark
{
    private const string EnvConn = "PAPERBASE_BENCH_PGCONN";
    private const int TopK = 5;

    private readonly ITestOutputHelper _output;

    public ProductionHybridSearchBenchmark(ITestOutputHelper output)
        => _output = output;

    [ProductionBenchmarkFact]
    public async Task Vector_vs_Hybrid_On_Desensitized_Corpus()
    {
        var connStr = Environment.GetEnvironmentVariable(EnvConn)!;

        var path = ProductionBenchmarkDataset.LocateDatasetPath();
        var dataset = ProductionBenchmarkDataset.Load(path);
        dataset.Validate();
        _output.WriteLine($"Dataset: {dataset.Chunks.Count} chunks, {dataset.Queries.Count} queries.");

        var benchmarkTenantId = Guid.NewGuid();
        // Slice C 起 chunks 与 documents 物理上属于不同 DbContext，但 benchmark 只验向量检索行为，
        // 仍可让两者共用同一物理库（同 connection string）。两个 context 各自持有连接，
        // SaveChanges 互不影响——不依赖 ABP UoW。
        await using var paperbaseDbContext = CreatePaperbaseDbContext(connStr);
        await using var pgvectorRagDbContext = CreatePgvectorRagDbContext(connStr);
        var vectorStore = CreateVectorStore(pgvectorRagDbContext);

        try
        {
            await SeedAsync(paperbaseDbContext, pgvectorRagDbContext, dataset, benchmarkTenantId);

            var vectorResults = await RunQueriesAsync(vectorStore, dataset, benchmarkTenantId, VectorSearchMode.Vector);
            var hybridResults = await RunQueriesAsync(vectorStore, dataset, benchmarkTenantId, VectorSearchMode.Hybrid);

            var vectorScores = ScoreByCategory(dataset, vectorResults, "Vector");
            var hybridScores = ScoreByCategory(dataset, hybridResults, "Hybrid");

            var table = BuildMarkdownTable(vectorScores.Concat(hybridScores).ToList());
            _output.WriteLine(table);
            EmitTableToDesignDoc(table);

            AssertThresholds(vectorScores, hybridScores);
        }
        finally
        {
            await CleanupAsync(paperbaseDbContext, pgvectorRagDbContext, benchmarkTenantId);
        }
    }

    private static PaperbaseDbContext CreatePaperbaseDbContext(string connStr)
    {
        var options = new DbContextOptionsBuilder<PaperbaseDbContext>()
            .UseNpgsql(connStr, o => o.UseVector())
            .Options;

        return new PaperbaseDbContext(options);
    }

    private static PgvectorRagDbContext CreatePgvectorRagDbContext(string connStr)
    {
        var options = new DbContextOptionsBuilder<PgvectorRagDbContext>()
            .UseNpgsql(connStr, o =>
            {
                o.UseVector();
                o.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
            })
            .Options;

        return new PgvectorRagDbContext(options);
    }

    private static IDocumentKnowledgeIndex CreateVectorStore(PgvectorRagDbContext dbContext)
    {
        return new PgvectorDocumentVectorStore(
            new StaticDbContextProvider(dbContext),
            new BenchmarkCurrentTenant(),
            Substitute.For<IDataFilter>());
    }

    // Seeding

    private static async Task SeedAsync(
        PaperbaseDbContext paperbaseDbContext,
        PgvectorRagDbContext pgvectorRagDbContext,
        ProductionBenchmarkDataset dataset,
        Guid tenantId)
    {
        var documentIds = SeedDocuments(paperbaseDbContext, dataset, tenantId);
        await paperbaseDbContext.SaveChangesAsync();

        SeedChunks(pgvectorRagDbContext, dataset, documentIds, tenantId);
        await pgvectorRagDbContext.SaveChangesAsync();
    }

    private static Dictionary<string, Guid> SeedDocuments(
        PaperbaseDbContext dbContext,
        ProductionBenchmarkDataset dataset,
        Guid tenantId)
    {
        var documentIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var typeCodes = dataset.Chunks
            .Select(c => c.DocumentTypeCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var typeCode in typeCodes)
        {
            var docId = Guid.NewGuid();
            documentIds[typeCode] = docId;

            var document = new Document(
                docId,
                tenantId,
                $"bench:{typeCode}:{docId:N}",
                SourceType.Digital,
                new FileOrigin(
                    uploadedByUserName: "benchmark",
                    contentType: "text/plain",
                    contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                    fileSize: 0,
                    originalFileName: $"benchmark-{typeCode}.txt"));

            typeof(Document)
                .GetProperty(nameof(Document.DocumentTypeCode))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(document, [typeCode]);

            dbContext.Set<Document>().Add(document);
        }

        return documentIds;
    }

    private static void SeedChunks(
        PgvectorRagDbContext dbContext,
        ProductionBenchmarkDataset dataset,
        IReadOnlyDictionary<string, Guid> documentIds,
        Guid tenantId)
    {
        var indexByDoc = new Dictionary<Guid, int>();

        foreach (var chunk in dataset.Chunks)
        {
            var docId = documentIds[chunk.DocumentTypeCode];
            if (!indexByDoc.TryGetValue(docId, out var idx))
            {
                idx = 0;
            }
            indexByDoc[docId] = idx + 1;

            dbContext.Set<DocumentChunk>().Add(new DocumentChunk(
                chunk.Id,
                tenantId,
                docId,
                idx,
                chunk.Text,
                chunk.DecodeEmbedding()));
        }
    }

    // Queries

    private static async Task<Dictionary<string, List<Guid>>> RunQueriesAsync(
        IDocumentKnowledgeIndex vectorStore,
        ProductionBenchmarkDataset dataset,
        Guid tenantId,
        VectorSearchMode mode)
    {
        var results = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);

        foreach (var query in dataset.Queries)
        {
            var searchResults = await vectorStore.SearchAsync(new VectorSearchRequest
            {
                TenantId = tenantId,
                QueryText = query.Text,
                QueryVector = query.DecodeEmbedding(),
                TopK = TopK,
                Mode = mode
            });

            AssertScoreContract(searchResults, mode, query.Id);
            results[query.Id] = searchResults
                .Select(r => r.RecordId)
                .ToList();
        }

        return results;
    }

    private static void AssertScoreContract(
        IReadOnlyList<VectorSearchResult> results,
        VectorSearchMode mode,
        string queryId)
    {
        foreach (var result in results)
        {
            result.Score.HasValue.ShouldBeTrue(
                $"{mode} result for query {queryId} did not include a score.");
            result.Score!.Value.ShouldBeInRange(
                0.0,
                1.0,
                $"{mode} score for query {queryId} must be normalized to [0, 1].");
        }
    }

    // Evaluation

    private static IReadOnlyList<RetrievalScores> ScoreByCategory(
        ProductionBenchmarkDataset dataset,
        Dictionary<string, List<Guid>> queryResults,
        string mode)
    {
        var categories = dataset.Queries
            .Select(q => q.Category)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return categories.Select(cat =>
        {
            var catQueries = dataset.Queries
                .Where(q => q.Category == cat)
                .ToList();

            var r1s = new List<double>();
            var r5s = new List<double>();
            var rrs = new List<double>();

            foreach (var q in catQueries)
            {
                var expected = new HashSet<Guid>(q.ExpectedChunkIds);
                var ranked = queryResults.TryGetValue(q.Id, out var r)
                    ? r.Select(g => g.ToString()).ToList()
                    : new List<string>();
                var expectedStr = new HashSet<string>(expected.Select(g => g.ToString()));

                r1s.Add(RetrievalMetrics.RecallAtK(ranked, expectedStr, 1));
                r5s.Add(RetrievalMetrics.RecallAtK(ranked, expectedStr, 5));
                rrs.Add(RetrievalMetrics.ReciprocalRank(ranked, expectedStr));
            }

            return new RetrievalScores
            {
                Mode = mode,
                Category = cat,
                QueryCount = catQueries.Count,
                RecallAt1 = RetrievalMetrics.Mean(r1s),
                RecallAt5 = RetrievalMetrics.Mean(r5s),
                Mrr = RetrievalMetrics.Mean(rrs)
            };
        }).ToList();
    }

    // Assertions

    private static void AssertThresholds(
        IReadOnlyList<RetrievalScores> vectorScores,
        IReadOnlyList<RetrievalScores> hybridScores)
    {
        var precVector = vectorScores.Single(s => s.Category == "precise-text");
        var precHybrid = hybridScores.Single(s => s.Category == "precise-text");
        var semVector = vectorScores.Single(s => s.Category == "semantic");
        var semHybrid = hybridScores.Single(s => s.Category == "semantic");

        var mrrLift = precHybrid.Mrr - precVector.Mrr;
        mrrLift.ShouldBeGreaterThanOrEqualTo(0.03,
            $"Precise-text MRR lift {mrrLift:F3} < 0.03. " +
            $"Vector={precVector.Mrr:F3} Hybrid={precHybrid.Mrr:F3}");

        var r1Lift = precHybrid.RecallAt1 - precVector.RecallAt1;
        r1Lift.ShouldBeGreaterThanOrEqualTo(0.03,
            $"Precise-text Recall@1 lift {r1Lift:F3} < 0.03. " +
            $"Vector={precVector.RecallAt1:F3} Hybrid={precHybrid.RecallAt1:F3}");

        var mrrReg = semVector.Mrr - semHybrid.Mrr;
        mrrReg.ShouldBeLessThanOrEqualTo(0.03,
            $"Semantic MRR regression {mrrReg:F3} > 0.03. " +
            $"Vector={semVector.Mrr:F3} Hybrid={semHybrid.Mrr:F3}");

        var r5Reg = semVector.RecallAt5 - semHybrid.RecallAt5;
        r5Reg.ShouldBeLessThanOrEqualTo(0.03,
            $"Semantic Recall@5 regression {r5Reg:F3} > 0.03. " +
            $"Vector={semVector.RecallAt5:F3} Hybrid={semHybrid.RecallAt5:F3}");
    }

    // Reporting

    private static string BuildMarkdownTable(IReadOnlyList<RetrievalScores> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Mode   | Category     | Queries | Recall@1 | Recall@5 | MRR    |");
        sb.AppendLine("|--------|--------------|---------|----------|----------|--------|");
        foreach (var r in rows.OrderBy(r => r.Category).ThenBy(r => r.Mode))
        {
            sb.AppendLine(
                $"| {r.Mode,-6} | {r.Category,-12} | {r.QueryCount,7} " +
                $"| {r.RecallAt1,8:F3} | {r.RecallAt5,8:F3} | {r.Mrr,6:F3} |");
        }
        return sb.ToString();
    }

    private void EmitTableToDesignDoc(string table)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            string? docPath = null;
            for (var i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "docs", "design", "rag-hybrid-benchmark-2026Q2.md");
                if (File.Exists(candidate))
                {
                    docPath = candidate;
                    break;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent is null || parent == dir)
                {
                    break;
                }
                dir = parent;
            }

            if (docPath is null)
            {
                _output.WriteLine("Design doc not found; skipping disk write.");
                return;
            }

            var content = File.ReadAllText(docPath);
            const string marker = "## Production Validation Results";
            var section = $"""

{marker}

*Run date: {DateTime.UtcNow:yyyy-MM-dd} UTC*

{table}
""";

            if (content.Contains(marker))
            {
                var start = content.IndexOf(marker, StringComparison.Ordinal);
                var end = content.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);
                content = end < 0
                    ? content[..start] + section
                    : content[..start] + section + content[end..];
            }
            else
            {
                content += section;
            }

            File.WriteAllText(docPath, content);
            _output.WriteLine($"Results written to {docPath}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: could not write to design doc: {ex.Message}");
        }
    }

    // Cleanup

    private static async Task CleanupAsync(
        PaperbaseDbContext paperbaseDbContext,
        PgvectorRagDbContext pgvectorRagDbContext,
        Guid tenantId)
    {
        await pgvectorRagDbContext.Set<DocumentChunk>()
            .Where(c => c.TenantId == tenantId)
            .ExecuteDeleteAsync();

        await paperbaseDbContext.Set<Document>()
            .Where(d => d.TenantId == tenantId)
            .ExecuteDeleteAsync();
    }

    private sealed class StaticDbContextProvider : IDbContextProvider<PgvectorRagDbContext>
    {
        private readonly PgvectorRagDbContext _dbContext;

        public StaticDbContextProvider(PgvectorRagDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public PgvectorRagDbContext GetDbContext()
        {
            return _dbContext;
        }

        public Task<PgvectorRagDbContext> GetDbContextAsync()
        {
            return Task.FromResult(_dbContext);
        }
    }

    private sealed class BenchmarkCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => Id.HasValue;

        public Guid? Id { get; private set; }

        public string? Name { get; private set; }

        public IDisposable Change(Guid? id, string? name = null)
        {
            Id = id;
            Name = name;
            return new NoopDisposable();
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

public sealed class ProductionBenchmarkFactAttribute : FactAttribute
{
    public ProductionBenchmarkFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAPERBASE_BENCH_PGCONN")))
        {
            Skip = "PAPERBASE_BENCH_PGCONN not set. Production benchmark skipped.";
            return;
        }

        try
        {
            ProductionBenchmarkDataset.LocateDatasetPath();
        }
        catch (FileNotFoundException ex)
        {
            Skip = ex.Message;
        }
    }
}
