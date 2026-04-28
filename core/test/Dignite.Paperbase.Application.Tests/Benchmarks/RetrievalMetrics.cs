using System.Collections.Generic;
using System.Linq;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Pure scoring helpers for retrieval evaluation. Used by the hybrid-search
/// benchmark to compare Vector vs Hybrid modes on the same query set.
///
/// Recall@K asks "did any expected chunk show up in the top K?" — a binary
/// per-query metric averaged across the query set. MRR (Mean Reciprocal Rank)
/// is continuous: <c>1/rank_of_first_hit</c>, averaged across queries (0 if no
/// expected chunk appears at all). MRR is the more sensitive of the two for
/// detecting "the right answer is in the results, but not at the top".
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>Recall@K for a single query. 1 if any expected chunk is in
    /// <paramref name="rankedIds"/>[..k], else 0.</summary>
    public static double RecallAtK(IReadOnlyList<string> rankedIds, ISet<string> expected, int k)
    {
        if (expected.Count == 0) return 0;
        var top = rankedIds.Take(k);
        return top.Any(expected.Contains) ? 1.0 : 0.0;
    }

    /// <summary>Reciprocal rank for a single query. 1/r where r is the 1-based
    /// position of the first expected chunk; 0 if none is found in <paramref name="rankedIds"/>.</summary>
    public static double ReciprocalRank(IReadOnlyList<string> rankedIds, ISet<string> expected)
    {
        for (var i = 0; i < rankedIds.Count; i++)
        {
            if (expected.Contains(rankedIds[i]))
                return 1.0 / (i + 1);
        }
        return 0.0;
    }

    /// <summary>Average a per-query metric across a result set.</summary>
    public static double Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }
}

/// <summary>Aggregate metrics for one (mode, category) cell of the benchmark
/// table. All scores are in [0, 1] — higher is better.</summary>
public sealed class RetrievalScores
{
    public string Mode { get; init; } = default!;
    public string Category { get; init; } = default!;
    public int QueryCount { get; init; }
    public double RecallAt1 { get; init; }
    public double RecallAt5 { get; init; }
    public double Mrr { get; init; }
}
