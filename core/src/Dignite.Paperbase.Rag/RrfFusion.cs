using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Reciprocal Rank Fusion: a provider-neutral helper for merging multiple ranked
/// candidate lists (e.g., dense vector + sparse BM25) into a single ranking.
///
/// Reference: Cormack, Clarke, Buettcher (2009), <em>"Reciprocal Rank Fusion outperforms
/// Condorcet and individual Rank Learning Methods"</em>.
///
/// Score formula: <c>RRF(d) = Σ 1 / (k + rank_i(d))</c>.
/// Items appearing only in one list still get a score; items in both get a higher
/// score. The merged scores are then min-max normalized to <c>[0, 1]</c> so
/// <see cref="VectorSearchResult.Score"/> remains mode-agnostic for callers.
///
/// Lives in the Rag layer (not in the Pgvector provider) because RRF is a pure
/// math operation independent of any storage backend — any provider that runs
/// dense + sparse paths separately can reuse this helper.
/// </summary>
public static class RrfFusion
{
    /// <summary>Default fusion constant; the value used in the original RRF paper.</summary>
    public const int DefaultK = 60;

    /// <summary>
    /// Merge two ranked lists with Reciprocal Rank Fusion and return the top
    /// <paramref name="topK"/> results, ordered by RRF score descending.
    /// Each result's <see cref="VectorSearchResult.Score"/> is min-max normalized
    /// to <c>[0, 1]</c> within the merged candidate set.
    /// </summary>
    /// <param name="primary">First ranked list (e.g., dense vector recall).</param>
    /// <param name="secondary">Second ranked list (e.g., sparse keyword recall).</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="k">RRF constant; larger values flatten the score curve.</param>
    public static IList<VectorSearchResult> Merge(
        IReadOnlyList<VectorSearchResult> primary,
        IReadOnlyList<VectorSearchResult> secondary,
        int topK,
        int k = DefaultK)
    {
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be positive.");
        if (k <= 0)
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be positive.");

        // Aggregate RRF scores keyed by RecordId. Any input list may be empty;
        // an item appearing in only one list still receives a (smaller) score.
        var aggregated = new Dictionary<Guid, FusionEntry>();

        Accumulate(aggregated, primary, k);
        Accumulate(aggregated, secondary, k);

        if (aggregated.Count == 0)
            return Array.Empty<VectorSearchResult>();

        // Min-max normalize the raw RRF scores to [0, 1]. Using min-max within the
        // candidate set (rather than the theoretical max 2/(k+1)) keeps the score
        // distribution legible to downstream MinScore filters even when no item
        // appears in both lists. When all items happen to have the same RRF score
        // (single-list case at rank 1), we return 1.0 for all to avoid /0.
        var maxScore = aggregated.Values.Max(e => e.RrfScore);
        var minScore = aggregated.Values.Min(e => e.RrfScore);
        var range = maxScore - minScore;

        var ordered = aggregated.Values
            .OrderByDescending(e => e.RrfScore)
            .Take(topK)
            .Select(e =>
            {
                var normalized = range > 0 ? (e.RrfScore - minScore) / range : 1.0;
                return new VectorSearchResult
                {
                    RecordId = e.Source.RecordId,
                    DocumentId = e.Source.DocumentId,
                    DocumentTypeCode = e.Source.DocumentTypeCode,
                    ChunkIndex = e.Source.ChunkIndex,
                    Text = e.Source.Text,
                    Score = normalized,
                    Title = e.Source.Title,
                    PageNumber = e.Source.PageNumber
                };
            })
            .ToList();

        return ordered;
    }

    private static void Accumulate(
        Dictionary<Guid, FusionEntry> aggregated,
        IReadOnlyList<VectorSearchResult> list,
        int k)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var rank = i + 1;
            var contribution = 1.0 / (k + rank);

            if (aggregated.TryGetValue(item.RecordId, out var existing))
            {
                existing.RrfScore += contribution;
            }
            else
            {
                aggregated[item.RecordId] = new FusionEntry
                {
                    Source = item,
                    RrfScore = contribution
                };
            }
        }
    }

    private sealed class FusionEntry
    {
        public VectorSearchResult Source { get; set; } = default!;
        public double RrfScore { get; set; }
    }
}
