using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents.Rag;
using Dignite.Paperbase.Rag;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents.AI;

/// <summary>
/// Bridges <see cref="IDocumentKnowledgeIndex"/> to Microsoft Agent Framework's
/// <see cref="TextSearchProvider"/>, so a <c>ChatClientAgent</c> can perform RAG
/// over Paperbase documents without re-implementing retrieval.
///
/// Pipeline: <c>ChatClientAgent → TextSearchProvider → DocumentTextSearchAdapter →
/// IDocumentKnowledgeIndex.SearchAsync(...)</c>. The adapter owns three jobs that
/// the framework can't do on its own:
/// <list type="bullet">
///   <item>Embed the query when the configured mode requires a vector (Vector / Hybrid),
///         and skip the embedding call entirely for Keyword mode (cost saving).</item>
///   <item>Carry an explicit <see cref="VectorSearchRequest.TenantId"/> so the search
///         is safe under Hangfire / CLI scenarios where ABP ambient context is absent.</item>
///   <item>Map Paperbase citation fields (<see cref="VectorSearchResult.Title"/>,
///         <see cref="VectorSearchResult.PageNumber"/>) onto Agent Framework's
///         <see cref="TextSearchProvider.TextSearchResult.SourceName"/> /
///         <see cref="TextSearchProvider.TextSearchResult.SourceLink"/> contract so
///         the agent can cite sources in its answer.</item>
/// </list>
///
/// This adapter does NOT replace <c>DocumentQaWorkflow</c> — it lives alongside it
/// as an optional path. Both share the same <see cref="IDocumentKnowledgeIndex"/>
/// implementation and benefit from any future hybrid-search / provider improvements.
/// </summary>
public class DocumentTextSearchAdapter : ITransientDependency
{
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly DocumentKnowledgeIndexSearchModeResolver _searchModeResolver;
    private readonly ICurrentTenant _currentTenant;
    private readonly PaperbaseRagOptions _ragOptions;

    public DocumentTextSearchAdapter(
        IDocumentKnowledgeIndex vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        DocumentKnowledgeIndexSearchModeResolver searchModeResolver,
        ICurrentTenant currentTenant,
        IOptions<PaperbaseRagOptions> ragOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _searchModeResolver = searchModeResolver;
        _currentTenant = currentTenant;
        _ragOptions = ragOptions.Value;
    }

    /// <summary>
    /// Build a <see cref="TextSearchProvider"/> scoped to the current ABP tenant.
    /// Use from HTTP-context / scoped DI consumers where <see cref="ICurrentTenant"/>
    /// is already aligned. For background jobs, use <see cref="CreateForTenant"/>
    /// with an explicit tenant id.
    /// </summary>
    public virtual TextSearchProvider CreateForCurrentTenant(
        TextSearchProviderOptions? providerOptions = null,
        DocumentSearchScope? scope = null)
        => CreateForTenant(_currentTenant.Id, providerOptions, scope);

    /// <summary>
    /// Build a <see cref="TextSearchProvider"/> scoped to an explicit tenant id.
    /// Pass <c>null</c> for host-level documents. The tenant id is captured in the
    /// returned provider's closure so subsequent invocations stay tenant-correct
    /// even if ABP ambient context drifts.
    /// </summary>
    public virtual TextSearchProvider CreateForTenant(
        Guid? tenantId,
        TextSearchProviderOptions? providerOptions = null,
        DocumentSearchScope? scope = null)
    {
        // Capture the parameters in a closure so the search delegate is self-contained.
        return new TextSearchProvider(
            (query, ct) => SearchAsync(tenantId, scope, query, ct),
            options: providerOptions);
    }

    /// <summary>
    /// The actual search delegate handed to <see cref="TextSearchProvider"/>.
    /// Embeds the query only when the mode needs a vector, so Keyword mode skips
    /// the embedding call (and its cost / latency) entirely.
    ///
    /// Public so it can be invoked directly for tests, custom Agent Framework
    /// integrations, or hosts that want the result list without going through
    /// <see cref="TextSearchProvider"/>'s context-injection lifecycle.
    /// </summary>
    public virtual async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(
        Guid? tenantId,
        DocumentSearchScope? scope,
        string query,
        CancellationToken cancellationToken = default)
    {
        var capabilities = _vectorStore.Capabilities;
        _searchModeResolver.EnsureSearchCapabilities(capabilities);
        var mode = _searchModeResolver.ResolveSearchMode(
            scope?.Mode ?? _ragOptions.DefaultSearchMode,
            capabilities);

        // Embedding generation is conditional: a Keyword-mode agent may run
        // without ever calling the embedding provider, which is the whole point
        // of letting Mode flow through.
        ReadOnlyMemory<float> queryVector = default;
        if (_searchModeResolver.RequiresVector(mode))
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [query], cancellationToken: cancellationToken);
            queryVector = embeddings[0].Vector;
        }

        var request = new VectorSearchRequest
        {
            TenantId = tenantId,
            QueryText = query,
            QueryVector = queryVector,
            TopK = scope?.TopK ?? _ragOptions.DefaultTopK,
            DocumentId = scope?.DocumentId,
            DocumentTypeCode = scope?.DocumentTypeCode,
            MinScore = capabilities.NormalizesScore ? scope?.MinScore ?? _ragOptions.MinScore : null,
            Mode = mode
        };

        var results = await _vectorStore.SearchAsync(request, cancellationToken);
        return results.Select(MapToTextSearchResult);
    }

    /// <summary>
    /// Map a Paperbase <see cref="VectorSearchResult"/> to the Agent Framework
    /// citation contract. <see cref="VectorSearchResult.Title"/> is the preferred
    /// human-readable source name; when missing we synthesize a stable identifier
    /// from <c>DocumentId</c> + <c>ChunkIndex</c> / <c>PageNumber</c> so the agent
    /// can still cite the chunk meaningfully.
    /// </summary>
    protected virtual TextSearchProvider.TextSearchResult MapToTextSearchResult(VectorSearchResult result)
    {
        return new TextSearchProvider.TextSearchResult
        {
            SourceName = result.Title ?? FormatDefaultSourceName(result),
            // No public URL scheme for Paperbase chunks yet; leaving null is the
            // honest answer. Hosts that expose a chunk-detail URL can subclass
            // this adapter and override the mapper.
            SourceLink = null,
            Text = result.Text
        };
    }

    /// <summary>
    /// Default source-name format when the chunk has no <see cref="VectorSearchResult.Title"/>.
    /// Prefers page number when present (most useful for human readers), falls back
    /// to chunk index. Override in a subclass to inject document type or filename
    /// once those become available on the result.
    /// </summary>
    protected virtual string FormatDefaultSourceName(VectorSearchResult result)
    {
        return result.PageNumber.HasValue
            ? $"Document {result.DocumentId} (page {result.PageNumber})"
            : $"Document {result.DocumentId} (chunk #{result.ChunkIndex})";
    }
}
