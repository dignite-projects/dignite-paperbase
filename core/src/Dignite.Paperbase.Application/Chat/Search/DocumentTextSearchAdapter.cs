using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// Bridges <see cref="IDocumentKnowledgeIndex"/> to Microsoft Agent Framework's
/// <see cref="TextSearchProvider"/>, so a <c>ChatClientAgent</c> can perform RAG
/// over Paperbase documents without re-implementing retrieval.
///
/// Pipeline: <c>ChatClientAgent → TextSearchProvider → DocumentTextSearchAdapter →
/// IDocumentKnowledgeIndex.SearchAsync(...)</c>. The adapter owns three jobs that
/// the framework can't do on its own:
/// <list type="bullet">
///   <item>Embed the query because Qdrant search requires a vector.</item>
///   <item>Carry an explicit <see cref="VectorSearchRequest.TenantId"/> so the search
///         is safe under Hangfire / CLI scenarios where ABP ambient context is absent.</item>
///   <item>Map Paperbase citation fields (<see cref="VectorSearchResult.PageNumber"/>)
///         onto Agent Framework's
///         <see cref="TextSearchProvider.TextSearchResult.SourceName"/> /
///         <see cref="TextSearchProvider.TextSearchResult.SourceLink"/> contract so
///         the agent can cite sources in its answer.</item>
/// </list>
///
/// This adapter is the shared document retrieval path used by document chat.
/// </summary>
public class DocumentTextSearchAdapter : ITransientDependency
{
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly DocumentRerankWorkflow _rerankWorkflow;
    private readonly ICurrentTenant _currentTenant;
    private readonly PaperbaseAIBehaviorOptions _aiOptions;
    private readonly PaperbaseKnowledgeIndexOptions _ragOptions;
    private readonly ILogger<DocumentTextSearchAdapter> _logger;

    public DocumentTextSearchAdapter(
        IDocumentKnowledgeIndex vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        DocumentRerankWorkflow rerankWorkflow,
        ICurrentTenant currentTenant,
        IOptions<PaperbaseAIBehaviorOptions> aiOptions,
        IOptions<PaperbaseKnowledgeIndexOptions> ragOptions,
        ILogger<DocumentTextSearchAdapter> logger)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _rerankWorkflow = rerankWorkflow;
        _currentTenant = currentTenant;
        _aiOptions = aiOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Build a <see cref="TextSearchProvider"/> and a paired <see cref="DocumentSearchCapture"/>
    /// scoped to the current ABP tenant. Use from HTTP-context / scoped DI consumers where
    /// <see cref="ICurrentTenant"/> is already aligned. For background jobs, use
    /// <see cref="CreateForTenant"/> with an explicit tenant id.
    /// </summary>
    public virtual (TextSearchProvider Provider, DocumentSearchCapture Capture) CreateForCurrentTenant(
        TextSearchProviderOptions? providerOptions = null,
        DocumentSearchScope? scope = null)
        => CreateForTenant(_currentTenant.Id, providerOptions, scope);

    /// <summary>
    /// Build a <see cref="TextSearchProvider"/> and a paired <see cref="DocumentSearchCapture"/>
    /// scoped to an explicit tenant id. The capture is fresh for each call — there is no shared
    /// instance state, so concurrent requests cannot cross-contaminate each other's results.
    ///
    /// After the agent invokes the provider's search delegate, <see cref="DocumentSearchCapture.LastResults"/>
    /// holds the <see cref="VectorSearchResult"/> list that was actually injected into the prompt.
    /// </summary>
    public virtual (TextSearchProvider Provider, DocumentSearchCapture Capture) CreateForTenant(
        Guid? tenantId,
        TextSearchProviderOptions? providerOptions = null,
        DocumentSearchScope? scope = null)
    {
        var capture = new DocumentSearchCapture();
        var options = BuildOptions(providerOptions, capture);
        var searchDelegate = CreateBoundSearchDelegate(tenantId, scope, capture);

        var provider = new TextSearchProvider(searchDelegate, options: options);
        return (provider, capture);
    }

    /// <summary>
    /// Creates the search delegate that fetches vector results, feeds <paramref name="capture"/>,
    /// and returns <see cref="TextSearchProvider.TextSearchResult"/> items. Factored out so
    /// subclasses and tests can invoke the capture-setting path directly without needing a
    /// <see cref="TextSearchProvider"/> wrapper.
    /// </summary>
    protected virtual Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>>
        CreateBoundSearchDelegate(Guid? tenantId, DocumentSearchScope? scope, DocumentSearchCapture capture)
    {
        return async (query, ct) =>
        {
            var vectorResults = await SearchVectorAsync(tenantId, scope, query, ct);
            capture.Set(vectorResults); // set before return so ContextFormatter sees results
            return vectorResults.Select(MapToTextSearchResult).ToList();
        };
    }

    /// <summary>
    /// The actual search delegate handed to <see cref="TextSearchProvider"/>.
    /// Public so it can be invoked directly for tests, custom Agent Framework
    /// integrations, or hosts that want the result list without going through
    /// <see cref="TextSearchProvider"/>'s context-injection lifecycle.
    /// Results are NOT captured by <see cref="DocumentSearchCapture"/> when called this way.
    /// </summary>
    public virtual async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(
        Guid? tenantId,
        DocumentSearchScope? scope,
        string query,
        CancellationToken cancellationToken = default)
    {
        var results = await SearchVectorAsync(tenantId, scope, query, cancellationToken);
        return results.Select(MapToTextSearchResult);
    }

    /// <summary>
    /// Formats the context block injected into the agent prompt. Each chunk is wrapped in
    /// <c>&lt;document id="…" chunk="…"&gt;</c> tags; the chunk text is passed through
    /// <see cref="PromptBoundary.WrapDocument"/> so that any <c>&lt;</c> characters are
    /// escaped to <c>&amp;lt;</c> before injection, preventing tag-injection attacks.
    /// Override in a subclass to customize the prompt structure.
    /// </summary>
    protected virtual string FormatSearchContext(
        IList<TextSearchProvider.TextSearchResult> textResults,
        IReadOnlyList<VectorSearchResult>? vectorResults)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < textResults.Count; i++)
        {
            var text = textResults[i].Text ?? string.Empty;
            var vr = vectorResults != null && i < vectorResults.Count ? vectorResults[i] : null;

            if (vr != null)
            {
                var pageAttr = vr.PageNumber.HasValue ? $" page=\"{vr.PageNumber}\"" : "";
                sb.AppendLine($"<document id=\"{vr.DocumentId:D}\" chunk=\"{vr.ChunkIndex}\"{pageAttr}>");
            }
            else
            {
                sb.AppendLine("<document>");
            }

            sb.AppendLine(PromptBoundary.WrapDocument(text));
            sb.AppendLine("</document>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Map a Paperbase <see cref="VectorSearchResult"/> to the Agent Framework citation
    /// contract. Source name is synthesized from <c>DocumentId</c> + <c>PageNumber</c>
    /// or <c>ChunkIndex</c> so the agent can cite the chunk meaningfully.
    /// </summary>
    protected virtual TextSearchProvider.TextSearchResult MapToTextSearchResult(VectorSearchResult result)
    {
        return new TextSearchProvider.TextSearchResult
        {
            SourceName = FormatDefaultSourceName(result),
            // No public URL scheme for Paperbase chunks yet; leaving null is the
            // honest answer. Hosts that expose a chunk-detail URL can subclass
            // this adapter and override the mapper.
            SourceLink = null,
            Text = result.Text
        };
    }

    /// <summary>
    /// Synthesizes a source name from the chunk's location metadata.
    /// Prefers page number when present, falls back to chunk index.
    /// Override in a subclass to inject document type or filename.
    /// </summary>
    protected virtual string FormatDefaultSourceName(VectorSearchResult result)
    {
        return result.PageNumber.HasValue
            ? $"Document {result.DocumentId} (page {result.PageNumber})"
            : $"Document {result.DocumentId} (chunk #{result.ChunkIndex})";
    }

    protected virtual async Task<IReadOnlyList<VectorSearchResult>> SearchVectorAsync(
        Guid? tenantId,
        DocumentSearchScope? scope,
        string query,
        CancellationToken cancellationToken = default)
    {
        var finalTopK = scope?.TopK ?? _ragOptions.DefaultTopK;
        var rerank = _aiOptions.EnableLlmRerank && finalTopK > 0;
        var recallTopK = rerank
            ? finalTopK * Math.Max(1, _aiOptions.RecallExpandFactor)
            : finalTopK;

        var embeddings = await _embeddingGenerator.GenerateAsync(
            [query], cancellationToken: cancellationToken);

        // DocumentIds (multi) supersedes DocumentId (single) when provided.
        var hasMultiIds = scope?.DocumentIds?.Count > 0;
        var request = new VectorSearchRequest
        {
            TenantId = tenantId,
            QueryVector = embeddings[0].Vector,
            TopK = recallTopK,
            DocumentId = hasMultiIds ? null : scope?.DocumentId,
            DocumentIds = hasMultiIds ? scope!.DocumentIds : null,
            DocumentTypeCode = scope?.DocumentTypeCode,
            MinScore = scope?.MinScore ?? _ragOptions.MinScore,
            QueryText = query
        };

        var results = await _vectorStore.SearchAsync(request, cancellationToken);
        if (!rerank || results.Count <= finalTopK)
        {
            return results.Take(finalTopK).ToList();
        }

        var candidates = results
            .Select(r => new RerankCandidate(r.Text, r.Score ?? 0.0, r))
            .ToList();

        var reranked = await _rerankWorkflow.RerankAsync(
            query,
            candidates,
            finalTopK,
            cancellationToken);

        return reranked
            .Select(r => (VectorSearchResult)r.Candidate.Tag!)
            .ToList();
    }

    /// <summary>
    /// Creates an <see cref="AIFunction"/> named <paramref name="functionName"/> that exposes
    /// vector search as an LLM-callable tool.  Used by <c>DocumentChatAppService</c> in
    /// <c>OnDemandFunctionCalling</c> mode instead of <c>TextSearchProvider</c>'s
    /// auto-generated function, because this variant accepts an optional <c>documentIds</c>
    /// parameter so the LLM can restrict the search to documents returned by earlier tool
    /// calls (e.g. <c>search_contracts</c> → <c>search_paperbase_documents</c>).
    ///
    /// <para>
    /// The returned function logs its call arguments and latency at Information level and
    /// sets <paramref name="capture"/> so that citations remain available after the turn.
    /// </para>
    /// </summary>
    public virtual AIFunction CreateSearchFunction(
        Guid? tenantId,
        DocumentSearchScope? baseScope,
        DocumentSearchCapture capture,
        string functionName,
        string functionDescription)
    {
        var binding = new SearchFunctionBinding(this, tenantId, baseScope, capture);
        return AIFunctionFactory.Create(
            binding.InvokeAsync,
            name: functionName,
            description: functionDescription);
    }

    protected virtual TextSearchProviderOptions BuildOptions(
        TextSearchProviderOptions? callerOptions,
        DocumentSearchCapture capture)
    {
        var opts = callerOptions ?? new TextSearchProviderOptions
        {
            RecentMessageMemoryLimit = 5
        };

        opts.ContextFormatter = results => FormatSearchContext(results, capture.LastResults);
        return opts;
    }

    // ── nested helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Holds the bound context for the <c>search_paperbase_documents</c> AIFunction.
    /// Factored into a class so parameter-level <see cref="DescriptionAttribute"/>s are
    /// accessible via reflection (lambda parameters cannot carry attributes in C#).
    /// </summary>
    private sealed class SearchFunctionBinding
    {
        private readonly DocumentTextSearchAdapter _adapter;
        private readonly Guid? _tenantId;
        private readonly DocumentSearchScope? _baseScope;
        private readonly DocumentSearchCapture _capture;

        public SearchFunctionBinding(
            DocumentTextSearchAdapter adapter,
            Guid? tenantId,
            DocumentSearchScope? baseScope,
            DocumentSearchCapture capture)
        {
            _adapter = adapter;
            _tenantId = tenantId;
            _baseScope = baseScope;
            _capture = capture;
        }

        public async Task<string> InvokeAsync(
            [Description("Search query text — describe what information you are looking for")]
            string query,
            [Description("Optional list of document IDs to restrict the search to. Pass IDs returned by other tools (e.g. search_contracts) to focus the RAG search on specific documents.")]
            Guid[]? documentIds = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            // Model-supplied documentIds narrow a type-scoped conversation to specific
            // documents (e.g. IDs returned by search_contracts → search_paperbase_documents).
            // When the conversation is already pinned to a single document
            // (_baseScope.DocumentId != null), the LLM cannot expand the authorized scope:
            // ignore documentIds entirely so the search stays within the original boundary.
            DocumentSearchScope? scope = documentIds?.Length > 0 && _baseScope?.DocumentId == null
                ? new DocumentSearchScope
                {
                    DocumentId = null,
                    DocumentIds = documentIds,
                    DocumentTypeCode = _baseScope?.DocumentTypeCode,
                    TopK = _baseScope?.TopK,
                    MinScore = _baseScope?.MinScore
                }
                : _baseScope;

            var vectorResults = await _adapter.SearchVectorAsync(_tenantId, scope, query, cancellationToken);
            _capture.Set(vectorResults);

            sw.Stop();
            _adapter._logger.LogInformation(
                "doc-chat search_paperbase_documents query={Query} documentIds={Ids} results={Count} latency={Latency}ms",
                query,
                documentIds == null ? "(none)" : string.Join(",", documentIds),
                vectorResults.Count,
                sw.ElapsedMilliseconds);

            var textResults = vectorResults.Select(_adapter.MapToTextSearchResult).ToList();
            return _adapter.FormatSearchContext(textResults, vectorResults);
        }
    }
}
