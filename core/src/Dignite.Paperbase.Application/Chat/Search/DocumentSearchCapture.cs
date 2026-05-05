using System.Collections.Generic;
using Dignite.Paperbase.KnowledgeIndex;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// Holds the <see cref="VectorSearchResult"/> list captured by a
/// <see cref="DocumentTextSearchAdapter"/> search invocation during one agent turn.
/// Created fresh per turn and bound by closure into the search AIFunction — never
/// shared between concurrent requests.
/// </summary>
public sealed class DocumentSearchCapture
{
    /// <summary>
    /// The vector search results from the most recent invocation of the
    /// search AIFunction. Null until the model invokes the search tool — when the
    /// model declines to search, this stays null and <c>ChatTurnResultDto.IsDegraded</c>
    /// surfaces the honest "no sources used" signal to the caller.
    /// </summary>
    public IReadOnlyList<VectorSearchResult>? LastResults { get; private set; }

    internal void Set(IReadOnlyList<VectorSearchResult> results) => LastResults = results;
}
