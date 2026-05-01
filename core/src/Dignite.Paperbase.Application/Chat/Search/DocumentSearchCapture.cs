using System.Collections.Generic;
using Dignite.Paperbase.KnowledgeIndex;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// Holds the <see cref="VectorSearchResult"/> list captured by a
/// <see cref="DocumentTextSearchAdapter"/> search delegate during one agent turn.
/// Created fresh by every <c>CreateForTenant</c> call and bound by closure — never
/// shared between concurrent requests.
/// </summary>
public sealed class DocumentSearchCapture
{
    /// <summary>
    /// The vector search results from the most recent search invoked through the
    /// associated <see cref="Microsoft.Agents.AI.TextSearchProvider"/>. Null until
    /// the first search completes.
    /// </summary>
    public IReadOnlyList<VectorSearchResult>? LastResults { get; private set; }

    internal void Set(IReadOnlyList<VectorSearchResult> results) => LastResults = results;
}
