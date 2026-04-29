using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Dignite.Paperbase.Rag.Benchmarks;

/// <summary>
/// Reproducible Vector vs Hybrid retrieval benchmark. Runs as a standard xUnit
/// Fact, so re-execution is just <c>dotnet test --filter HybridSearchBenchmark</c>;
/// no console runner or live infrastructure needed.
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
///   <item>Real embedding model behavior (we use bigram cosine, not dense vectors).</item>
///   <item>Real provider keyword search behavior (we use plain token-overlap fraction).</item>
///   <item>Recall saturation under millions of chunks.</item>
/// </list>
/// Production validation against desensitized corpus + real LLM + real provider is the
/// follow-up of <see href="https://github.com/dignite-projects/dignite-paperbase/issues/30">#30</see>.
///
/// Acceptance gates (assertions below). Note that recall@5 saturates at 1.0
/// for both modes at this corpus size (N=30 chunks), so the hybrid lift shows
/// up in MRR and recall@1 instead — those are the discriminating metrics here.
/// <list type="number">
///   <item>Hybrid MRR on precise-text ≥ Vector MRR + <c>0.03</c>.</item>
///   <item>Hybrid recall@1 on precise-text ≥ Vector recall@1 + <c>0.03</c>.</item>
///   <item>Hybrid MRR on semantic ≥ Vector MRR − <c>0.03</c> (regression budget).</item>
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

        _output.WriteLine(table);
        EmitTableToDisk(table);

        var precVector = Get(vectorScores, "precise-text");
        var precHybrid = Get(hybridScores, "precise-text");
        var semVector = Get(vectorScores, "semantic");
        var semHybrid = Get(hybridScores, "semantic");

        var mrrLift = precHybrid.Mrr - precVector.Mrr;
        mrrLift.ShouldBeGreaterThanOrEqualTo(
            0.03,
            $"Precise-text hybrid MRR lift was {mrrLift:F3}; expected >= 0.03. " +
            $"Vector={precVector.Mrr:F3}, Hybrid={precHybrid.Mrr:F3}.");

        var r1Lift = precHybrid.RecallAt1 - precVector.RecallAt1;
        r1Lift.ShouldBeGreaterThanOrEqualTo(
            0.03,
            $"Precise-text hybrid recall@1 lift was {r1Lift:F3}; expected >= 0.03. " +
            $"Vector={precVector.RecallAt1:F3}, Hybrid={precHybrid.RecallAt1:F3}.");

        var mrrRegression = semVector.Mrr - semHybrid.Mrr;
        mrrRegression.ShouldBeLessThanOrEqualTo(
            0.03,
            $"Semantic MRR regressed by {mrrRegression:F3}; budget is 0.03. " +
            $"Vector={semVector.Mrr:F3}, Hybrid={semHybrid.Mrr:F3}.");

        var r5Regression = semVector.RecallAt5 - semHybrid.RecallAt5;
        r5Regression.ShouldBeLessThanOrEqualTo(
            0.03,
            $"Semantic recall@5 regressed by {r5Regression:F3}; budget is 0.03. " +
            $"Vector={semVector.RecallAt5:F3}, Hybrid={semHybrid.RecallAt5:F3}.");

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
