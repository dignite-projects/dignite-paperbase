using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;
using Npgsql;
using Shouldly;
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
/// The test skips silently when <c>PAPERBASE_BENCH_PGCONN</c> is not set, so it
/// never interferes with the regular CI test suite.
///
/// See <c>docs/benchmarks/README.md</c> for dataset preparation instructions.
/// </summary>
[Trait("Category", "Production")]
public class ProductionHybridSearchBenchmark
{
    private const string EnvConn = "PAPERBASE_BENCH_PGCONN";
    private const int TopK = 5;
    private const int HybridRecallMultiplier = 2;

    // Table names match PaperbaseDbProperties.DbTablePrefix + entity suffix.
    private const string ChunkTable = "\"PaperbaseDocumentChunks\"";
    private const string DocumentTable = "\"PaperbaseDocuments\"";

    private readonly ITestOutputHelper _output;

    public ProductionHybridSearchBenchmark(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public async Task Vector_vs_Hybrid_On_Desensitized_Corpus()
    {
        var connStr = Environment.GetEnvironmentVariable(EnvConn);
        if (connStr is null)
        {
            _output.WriteLine($"SKIP: {EnvConn} not set. See docs/benchmarks/README.md.");
            return;
        }

        ProductionBenchmarkDataset dataset;
        try
        {
            var path = ProductionBenchmarkDataset.LocateDatasetPath();
            dataset = ProductionBenchmarkDataset.Load(path);
            _output.WriteLine($"Dataset: {dataset.Chunks.Count} chunks, {dataset.Queries.Count} queries.");
        }
        catch (FileNotFoundException ex)
        {
            _output.WriteLine($"SKIP: {ex.Message}");
            return;
        }

        if (dataset.Chunks.Count == 0 || dataset.Queries.Count == 0)
        {
            _output.WriteLine("SKIP: Dataset is empty.");
            return;
        }

        var seedDocIds = new List<Guid>();
        var seedChunkIds = dataset.Chunks.Select(c => c.Id).ToList();

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        try
        {
            seedDocIds = await SeedDocumentsAsync(conn, dataset);
            await SeedChunksAsync(conn, dataset, seedDocIds);

            var vectorResults = await RunVectorQueriesAsync(conn, dataset, seedChunkIds, TopK);
            var hybridResults = await RunHybridQueriesAsync(conn, dataset, seedChunkIds, TopK);

            var vectorScores = ScoreByCategory(dataset, vectorResults, "Vector");
            var hybridScores = ScoreByCategory(dataset, hybridResults, "Hybrid");

            var table = BuildMarkdownTable(vectorScores.Concat(hybridScores).ToList());
            _output.WriteLine(table);
            EmitTableToDesignDoc(table);

            AssertThresholds(vectorScores, hybridScores);
        }
        finally
        {
            await CleanupAsync(conn, seedDocIds, seedChunkIds);
        }
    }

    // ── Seeding ────────────────────────────────────────────────────────────

    private static async Task<List<Guid>> SeedDocumentsAsync(
        NpgsqlConnection conn, ProductionBenchmarkDataset dataset)
    {
        // One Document row per distinct DocumentTypeCode — chunks share a document.
        var docIds = new List<Guid>();
        var typeCodes = dataset.Chunks
            .Select(c => c.DocumentTypeCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var typeCode in typeCodes)
        {
            var docId = Guid.NewGuid();
            docIds.Add(docId);

            // Use a content hash that is unique per run via the docId.
            var contentHash = docId.ToString("N")[..32];

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {DocumentTable} (
                    "Id", "TenantId", "OriginalFileBlobName", "SourceType",
                    "FileOrigin_UploadedByUserName", "FileOrigin_OriginalFileName",
                    "FileOrigin_ContentType", "FileOrigin_FileSize",
                    "DocumentTypeCode", "LifecycleStatus", "ConfidenceScore",
                    "ExtraProperties", "ConcurrencyStamp", "CreationTime"
                ) VALUES (
                    $1, NULL, $2, 2,
                    'benchmark', $3,
                    'text/plain', 0,
                    $4, 10, 0.0,
                    $5, $6, NOW()
                )
                """;
            cmd.Parameters.AddWithValue(docId);
            cmd.Parameters.AddWithValue($"bench:{typeCode}:{docId:N}");
            cmd.Parameters.AddWithValue($"benchmark-{typeCode}.txt");
            cmd.Parameters.AddWithValue(typeCode);
            cmd.Parameters.AddWithValue("{}");
            cmd.Parameters.AddWithValue(Guid.NewGuid().ToString("N"));

            await cmd.ExecuteNonQueryAsync();
        }

        return docIds;
    }

    private static async Task SeedChunksAsync(
        NpgsqlConnection conn,
        ProductionBenchmarkDataset dataset,
        IReadOnlyList<Guid> docIds)
    {
        // Map each DocumentTypeCode to its seeded document id.
        var typeCodes = dataset.Chunks
            .Select(c => c.DocumentTypeCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var typeCodeToDocId = typeCodes
            .Zip(docIds, (tc, id) => (tc, id))
            .ToDictionary(x => x.tc, x => x.id);

        var indexByDoc = new Dictionary<Guid, int>();

        foreach (var chunk in dataset.Chunks)
        {
            var docId = typeCodeToDocId[chunk.DocumentTypeCode];
            if (!indexByDoc.TryGetValue(docId, out var idx)) idx = 0;
            indexByDoc[docId] = idx + 1;

            var vectorLiteral = chunk.ToVectorLiteral();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {ChunkTable} (
                    "Id", "TenantId", "DocumentId", "ChunkIndex",
                    "ChunkText", "EmbeddingVector",
                    "ExtraProperties", "ConcurrencyStamp", "CreationTime"
                ) VALUES (
                    $1, NULL, $2, $3,
                    $4, $5::vector,
                    $6, $7, NOW()
                )
                """;
            cmd.Parameters.AddWithValue(chunk.Id);
            cmd.Parameters.AddWithValue(docId);
            cmd.Parameters.AddWithValue(idx);
            cmd.Parameters.AddWithValue(chunk.Text);
            cmd.Parameters.AddWithValue(vectorLiteral);
            cmd.Parameters.AddWithValue("{}");
            cmd.Parameters.AddWithValue(Guid.NewGuid().ToString("N"));

            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Queries ────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, List<Guid>>> RunVectorQueriesAsync(
        NpgsqlConnection conn,
        ProductionBenchmarkDataset dataset,
        IReadOnlyList<Guid> seedChunkIds,
        int topK)
    {
        var results = new Dictionary<string, List<Guid>>();
        var chunkIdArray = seedChunkIds.ToArray();

        foreach (var query in dataset.Queries)
        {
            var vectorLiteral = query.ToVectorLiteral();
            if (vectorLiteral == "[]")
            {
                results[query.Id] = new List<Guid>();
                continue;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT c."Id"
                FROM {ChunkTable} c
                WHERE c."Id" = ANY($1)
                  AND c."TenantId" IS NULL
                ORDER BY c."EmbeddingVector" <=> $2::vector
                LIMIT $3
                """;
            cmd.Parameters.AddWithValue(chunkIdArray);
            cmd.Parameters.AddWithValue(vectorLiteral);
            cmd.Parameters.AddWithValue(topK);

            var ranked = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ranked.Add(reader.GetGuid(0));

            results[query.Id] = ranked;
        }

        return results;
    }

    private static async Task<Dictionary<string, List<Guid>>> RunHybridQueriesAsync(
        NpgsqlConnection conn,
        ProductionBenchmarkDataset dataset,
        IReadOnlyList<Guid> seedChunkIds,
        int topK)
    {
        var results = new Dictionary<string, List<Guid>>();
        var chunkIdArray = seedChunkIds.ToArray();
        var recallTopK = topK * HybridRecallMultiplier;

        foreach (var query in dataset.Queries)
        {
            var vectorRanked = await QueryVectorRawAsync(conn, query, chunkIdArray, recallTopK);
            var keywordRanked = await QueryKeywordRawAsync(conn, query, chunkIdArray, recallTopK);

            // RRF merge (same code as production PgvectorDocumentVectorStore).
            var merged = RrfFusion.Merge(vectorRanked, keywordRanked, topK);
            results[query.Id] = merged
                .Select(r => r.RecordId)
                .ToList();
        }

        return results;
    }

    private static async Task<IReadOnlyList<VectorSearchResult>> QueryVectorRawAsync(
        NpgsqlConnection conn, ProductionQuery query, Guid[] chunkIdArray, int topK)
    {
        var vectorLiteral = query.ToVectorLiteral();
        if (vectorLiteral == "[]")
            return Array.Empty<VectorSearchResult>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c."Id",
                   1.0 - (c."EmbeddingVector" <=> $1::vector) AS score
            FROM {ChunkTable} c
            WHERE c."Id" = ANY($2)
              AND c."TenantId" IS NULL
            ORDER BY c."EmbeddingVector" <=> $1::vector
            LIMIT $3
            """;
        cmd.Parameters.AddWithValue(vectorLiteral);
        cmd.Parameters.AddWithValue(chunkIdArray);
        cmd.Parameters.AddWithValue(topK);

        var results = new List<VectorSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VectorSearchResult
            {
                RecordId = reader.GetGuid(0),
                DocumentId = reader.GetGuid(0),
                Score = reader.GetDouble(1)
            });
        }
        return results;
    }

    private static async Task<IReadOnlyList<VectorSearchResult>> QueryKeywordRawAsync(
        NpgsqlConnection conn, ProductionQuery query, Guid[] chunkIdArray, int topK)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
            return Array.Empty<VectorSearchResult>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c."Id",
                   ts_rank_cd(c."SearchVector", plainto_tsquery('simple', $1)) AS rank
            FROM {ChunkTable} c
            WHERE c."SearchVector" @@ plainto_tsquery('simple', $1)
              AND c."Id" = ANY($2)
              AND c."TenantId" IS NULL
            ORDER BY rank DESC
            LIMIT $3
            """;
        cmd.Parameters.AddWithValue(query.Text);
        cmd.Parameters.AddWithValue(chunkIdArray);
        cmd.Parameters.AddWithValue(topK);

        var rows = new List<(Guid id, double rank)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetGuid(0), reader.GetDouble(1)));

        if (rows.Count == 0)
            return Array.Empty<VectorSearchResult>();

        // Min-max normalize (same as PgvectorDocumentVectorStore.NormalizeMinMax).
        var max = rows.Max(r => r.rank);
        var min = rows.Min(r => r.rank);
        var range = max - min;

        return rows.Select(r => new VectorSearchResult
        {
            RecordId = r.id,
            DocumentId = r.id,
            Score = range > 0 ? (r.rank - min) / range : 1.0
        }).ToList();
    }

    // ── Evaluation ─────────────────────────────────────────────────────────

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
                var rankedStr = ranked;
                var expectedStr = new HashSet<string>(expected.Select(g => g.ToString()));

                r1s.Add(RetrievalMetrics.RecallAtK(rankedStr, expectedStr, 1));
                r5s.Add(RetrievalMetrics.RecallAtK(rankedStr, expectedStr, 5));
                rrs.Add(RetrievalMetrics.ReciprocalRank(rankedStr, expectedStr));
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

    // ── Assertions ─────────────────────────────────────────────────────────

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

    // ── Reporting ──────────────────────────────────────────────────────────

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
            // Locate docs/design/rag-hybrid-benchmark-2026Q2.md relative to repo root.
            var dir = AppContext.BaseDirectory;
            string? docPath = null;
            for (var i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "docs", "design", "rag-hybrid-benchmark-2026Q2.md");
                if (File.Exists(candidate)) { docPath = candidate; break; }
                var parent = Path.GetDirectoryName(dir);
                if (parent is null || parent == dir) break;
                dir = parent;
            }

            if (docPath is null)
            {
                _output.WriteLine("Design doc not found — skipping disk write.");
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
                // Replace existing section up to the next `##` heading or end of file.
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

    // ── Cleanup ────────────────────────────────────────────────────────────

    private static async Task CleanupAsync(
        NpgsqlConnection conn,
        IReadOnlyList<Guid> docIds,
        IReadOnlyList<Guid> chunkIds)
    {
        if (chunkIds.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {ChunkTable} WHERE \"Id\" = ANY($1)";
            cmd.Parameters.AddWithValue(chunkIds.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }

        // DocumentChunks FK has ON DELETE CASCADE from Documents, but we delete
        // chunks explicitly first so the Document DELETE doesn't trigger bulk cascade.
        if (docIds.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {DocumentTable} WHERE \"Id\" = ANY($1)";
            cmd.Parameters.AddWithValue(docIds.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
