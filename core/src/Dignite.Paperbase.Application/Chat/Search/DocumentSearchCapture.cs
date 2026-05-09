using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.KnowledgeIndex;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// Accumulates <see cref="VectorSearchResult"/>s captured by every invocation of the
/// search AIFunction during one agent turn. Created fresh per turn and bound by
/// closure into the search AIFunction — never shared between concurrent requests.
/// </summary>
/// <remarks>
/// The model may invoke the search tool multiple times per turn (e.g. once on contracts,
/// then a second time on receipts after a structured tool returned candidate IDs). The
/// previous <c>Set</c> name suggested overwrite semantics — the body was already
/// append+dedup, but the name was a footgun. The class is now explicitly append-only,
/// dedups by chunk identity, and enforces an upper bound (<see cref="MaxResults"/>)
/// to bound prompt-context growth and prevent pathological retry loops from blowing up
/// the citations payload.
/// </remarks>
public sealed class DocumentSearchCapture
{
    /// <summary>
    /// Default per-turn citation cap, used when no explicit limit is supplied.
    /// Aligned with <c>PaperbaseAIBehaviorOptions.MaxCapturedCitations</c>; the
    /// AppService normally constructs the capture with the configured value, so this
    /// constant is mainly here for tests and stand-alone usage.
    /// </summary>
    public const int DefaultMaxResults = 50;

    private readonly List<VectorSearchResult> _results = new();

    public DocumentSearchCapture(int maxResults = DefaultMaxResults)
    {
        // <= 0 disables the cap (treated as unbounded). Keeps the test helpers in
        // DocumentTextSearchAdapter_Tests trivially compatible — they construct
        // captures without an explicit limit.
        MaxResults = maxResults;
    }

    /// <summary>
    /// Hard upper bound on the number of distinct chunks retained across all search
    /// invocations in one turn. Excess hits are silently dropped after the bound is
    /// reached and <see cref="WasTruncated"/> is set so the caller can surface the
    /// signal in telemetry.
    /// </summary>
    public int MaxResults { get; }

    /// <summary>
    /// All vector search results captured during this turn, accumulated across every
    /// invocation of the search AIFunction. The model may call search more than once
    /// per turn (e.g. to chain a structured-tool result into a focused RAG pass), and
    /// citations must reflect the union of those calls — not just the last one.
    /// </summary>
    public IReadOnlyList<VectorSearchResult> Results => _results;

    /// <summary>
    /// <c>true</c> after the search AIFunction is invoked at least once, even if that
    /// invocation returned no hits. Distinguishes "model declined to search" (false)
    /// from "model searched but found nothing" (true). Used by the AppService together
    /// with the per-turn grounding source to decide
    /// <see cref="ChatTurnResultDto.IsDegraded"/>.
    /// </summary>
    public bool HasSearches { get; private set; }

    /// <summary>
    /// <c>true</c> if at least one search hit was dropped because the cumulative count
    /// would have exceeded <see cref="MaxResults"/>. Lets the AppService record a
    /// <c>citations_trimmed</c> telemetry signal — useful for spotting pathological
    /// LLM retry loops or queries that fan out into very large recall sets.
    /// </summary>
    public bool WasTruncated { get; private set; }

    internal void Append(IReadOnlyList<VectorSearchResult> results)
    {
        HasSearches = true;

        foreach (var result in results)
        {
            if (_results.Any(existing => IsSameChunk(existing, result)))
                continue;

            if (MaxResults > 0 && _results.Count >= MaxResults)
            {
                WasTruncated = true;
                // Bail on the rest of this batch as well — preserving the natural
                // ordering of the first MaxResults hits is more useful for citations
                // than mixing partial later batches.
                return;
            }

            _results.Add(result);
        }
    }

    private static bool IsSameChunk(VectorSearchResult left, VectorSearchResult right)
    {
        if (left.RecordId != default && right.RecordId != default)
            return left.RecordId == right.RecordId;

        return left.DocumentId == right.DocumentId
            && left.ChunkIndex == right.ChunkIndex
            && left.PageNumber == right.PageNumber;
    }
}
