using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Reproducible Vector vs Hybrid retrieval benchmark for Slice 7. Runs as a
/// standard xUnit fact, so re-execution is just <c>dotnet test --filter
/// HybridSearchBenchmark</c>; no console runner or live infrastructure needed.
///
/// What this benchmark validates:
/// <list type="bullet">
///   <item>RRF math + mode dispatch in the production <see cref="RrfFusion"/>
///         helper produce the expected ranking lift on precise-text queries
///         (rare IDs, names) without regressing semantic queries.</item>
///   <item>The contract that <see cref="VectorSearchResult.Score"/> stays in
///         <c>[0, 1]</c> across all modes after fusion / normalization.</item>
/// </list>
///
/// What it deliberately does NOT validate:
/// <list type="bullet">
///   <item>Real Embedding model behavior (we use bigram cosine, not 1536-dim
///         dense vectors).</item>
///   <item>Real PostgreSQL <c>ts_rank_cd</c> / IDF behavior (we use plain
///         token-overlap fraction).</item>
///   <item>Recall saturation under millions of chunks.</item>
/// </list>
/// Production validation against脱敏 corpus + real LLM + Postgres is the
/// follow-up of <see href="https://github.com/dignite-projects/dignite-paperbase/issues/30">#30</see>.
///
/// Acceptance gates (assertions below). Note that recall@5 saturates at 1.0
/// for both modes at this corpus size (N=30 chunks), so the hybrid lift shows
/// up in MRR and recall@1 instead — those are the discriminating metrics here.
/// In a production-scale corpus (thousands of chunks) recall@5 would also
/// differentiate; that case is the follow-up real-data benchmark.
/// <list type="number">
///   <item>Hybrid MRR on precise-text ≥ Vector MRR + <c>0.03</c>
///         (rare-ID queries should land at #1 more often).</item>
///   <item>Hybrid recall@1 on precise-text ≥ Vector recall@1 + <c>0.03</c>.</item>
///   <item>Hybrid MRR on semantic ≥ Vector MRR − <c>0.03</c>
///         (regression budget for fusion noise).</item>
///   <item>Hybrid recall@5 on semantic ≥ Vector recall@5 − <c>0.03</c>.</item>
///   <item>All Score values in [0, 1] regardless of mode.</item>
/// </list>
/// </summary>
public class HybridSearchBenchmark
{
    private const int TopK = 5;

    private readonly ITestOutputHelper _output;

    public HybridSearchBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Vector_vs_Hybrid_On_Synthetic_Corpus()
    {
        var store = new InMemoryHybridDocumentVectorStore();
        store.Seed(BenchmarkDataset.Chunks);

        var vectorScores = await EvaluateAsync(store, VectorSearchMode.Vector);
        var hybridScores = await EvaluateAsync(store, VectorSearchMode.Hybrid);

        var allScores = vectorScores.Concat(hybridScores).ToList();
        var table = BuildMarkdownTable(allScores);

        // xUnit captures stdout; the table is also written to disk so it's easy
        // to diff between runs / paste into the design report.
        _output.WriteLine(table);
        EmitTableToDisk(table);

        // ── Assertion gates ─────────────────────────────────────────────
        var precVector = Get(vectorScores, "precise-text");
        var precHybrid = Get(hybridScores, "precise-text");
        var semVector = Get(vectorScores, "semantic");
        var semHybrid = Get(hybridScores, "semantic");

        // Gate 1: hybrid lifts precise-text MRR. MRR is the most sensitive
        // metric here — recall@5 saturates at 1.0 because the corpus is small,
        // but MRR captures whether the right chunk lands at rank 1 vs further down.
        var mrrLift = precHybrid.Mrr - precVector.Mrr;
        mrrLift.ShouldBeGreaterThanOrEqualTo(
            0.03,
            $"Precise-text hybrid MRR lift was {mrrLift:F3}; expected >= 0.03. " +
            $"Vector={precVector.Mrr:F3}, Hybrid={precHybrid.Mrr:F3}.");

        // Gate 2: hybrid lifts precise-text recall@1. Same shape as Gate 1, but
        // checks the strict top-1 case so a regression to "right answer at #2"
        // would be caught even if MRR averaged out across queries.
        var r1Lift = precHybrid.RecallAt1 - precVector.RecallAt1;
        r1Lift.ShouldBeGreaterThanOrEqualTo(
            0.03,
            $"Precise-text hybrid recall@1 lift was {r1Lift:F3}; expected >= 0.03. " +
            $"Vector={precVector.RecallAt1:F3}, Hybrid={precHybrid.RecallAt1:F3}.");

        // Gate 3: hybrid does not regress semantic MRR by more than 3 pp. RRF
        // can shuffle a previously-top-1 dense hit if the sparse path strongly
        // disagrees, but the budget keeps the regression minor.
        var mrrRegression = semVector.Mrr - semHybrid.Mrr;
        mrrRegression.ShouldBeLessThanOrEqualTo(
            0.03,
            $"Semantic MRR regressed by {mrrRegression:F3}; budget is 0.03. " +
            $"Vector={semVector.Mrr:F3}, Hybrid={semHybrid.Mrr:F3}.");

        // Gate 4: hybrid does not regress semantic recall@5 by more than 3 pp.
        var r5Regression = semVector.RecallAt5 - semHybrid.RecallAt5;
        r5Regression.ShouldBeLessThanOrEqualTo(
            0.03,
            $"Semantic recall@5 regressed by {r5Regression:F3}; budget is 0.03. " +
            $"Vector={semVector.RecallAt5:F3}, Hybrid={semHybrid.RecallAt5:F3}.");

        // Gate 5: every result must respect the [0, 1] Score contract. The
        // Application-layer MinScore filter assumes this invariant.
        await ScoreRangeInvariantHolds(store, VectorSearchMode.Vector);
        await ScoreRangeInvariantHolds(store, VectorSearchMode.Hybrid);
    }

    private static async Task<IReadOnlyList<RetrievalScores>> EvaluateAsync(
        InMemoryHybridDocumentVectorStore store, VectorSearchMode mode)
    {
        var perCategory = new Dictionary<QueryCategory, List<(double r1, double r5, double rr)>>
        {
            [QueryCategory.PreciseText] = new(),
            [QueryCategory.Semantic] = new()
        };

        foreach (var query in BenchmarkDataset.Queries)
        {
            var request = new VectorSearchRequest
            {
                QueryText = query.Text,
                TopK = TopK,
                Mode = mode
            };
            var results = await store.SearchAsync(request, CancellationToken.None);

            // The synthetic store stores its chunk id in DocumentTypeCode for assertion lookup
            // (production VectorSearchResult.DocumentTypeCode comes from the document join).
            var rankedIds = results.Select(r => r.DocumentTypeCode!).ToList();

            perCategory[query.Category].Add((
                RetrievalMetrics.RecallAtK(rankedIds, query.ExpectedChunkIds, 1),
                RetrievalMetrics.RecallAtK(rankedIds, query.ExpectedChunkIds, 5),
                RetrievalMetrics.ReciprocalRank(rankedIds, query.ExpectedChunkIds)));
        }

        return perCategory
            .Select(kv => new RetrievalScores
            {
                Mode = mode.ToString(),
                Category = kv.Key == QueryCategory.PreciseText ? "precise-text" : "semantic",
                QueryCount = kv.Value.Count,
                RecallAt1 = RetrievalMetrics.Mean(kv.Value.Select(t => t.r1)),
                RecallAt5 = RetrievalMetrics.Mean(kv.Value.Select(t => t.r5)),
                Mrr = RetrievalMetrics.Mean(kv.Value.Select(t => t.rr))
            })
            .ToList();
    }

    private static async Task ScoreRangeInvariantHolds(
        InMemoryHybridDocumentVectorStore store, VectorSearchMode mode)
    {
        foreach (var query in BenchmarkDataset.Queries)
        {
            var request = new VectorSearchRequest { QueryText = query.Text, TopK = TopK, Mode = mode };
            var results = await store.SearchAsync(request, CancellationToken.None);
            results.ShouldAllBe(
                r => r.Score >= 0 && r.Score <= 1,
                $"Mode={mode}, query=\"{query.Text}\" produced a score outside [0, 1]");
        }
    }

    private static RetrievalScores Get(IEnumerable<RetrievalScores> set, string category)
        => set.Single(s => s.Category == category);

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

    private static void EmitTableToDisk(string table)
    {
        // Write next to the test assembly so subsequent builds keep the freshest
        // numbers on disk. Path is relative to the test bin/ folder, intentionally
        // outside the repo to avoid noise on every CI run.
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "hybrid-benchmark-results.md");
            File.WriteAllText(path, table);
        }
        catch
        {
            // Disk writes are best-effort — assertions are the source of truth.
        }
    }
}
