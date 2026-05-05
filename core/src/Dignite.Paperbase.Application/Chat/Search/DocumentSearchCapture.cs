using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.KnowledgeIndex;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// Accumulates <see cref="VectorSearchResult"/>s captured by every invocation of the
/// search AIFunction during one agent turn. Created fresh per turn and bound by
/// closure into the search AIFunction — never shared between concurrent requests.
/// </summary>
public sealed class DocumentSearchCapture
{
    private readonly List<VectorSearchResult> _results = new();

    /// <summary>
    /// All vector search results captured during this turn, accumulated across every
    /// invocation of the search AIFunction. The model may call search more than once
    /// per turn (e.g. to chain a structured-tool result into a focused RAG pass), and
    /// citations must reflect the union of those calls — not just the last one.
    /// </summary>
    public IReadOnlyList<VectorSearchResult> Results => _results;

    /// <summary>
    /// <c>true</c> after the search AIFunction is invoked at least once, even if that
    /// invocation returned no hits. Distinguishes "model declined to search" (false →
    /// <c>ChatTurnResultDto.IsDegraded = true</c>; answer ungrounded) from "model
    /// searched but found nothing" (true → IsDegraded = false; honest empty citations).
    /// </summary>
    public bool HasSearches { get; private set; }

    internal void Set(IReadOnlyList<VectorSearchResult> results)
    {
        HasSearches = true;

        foreach (var result in results)
        {
            if (_results.Any(existing => IsSameChunk(existing, result)))
                continue;

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
