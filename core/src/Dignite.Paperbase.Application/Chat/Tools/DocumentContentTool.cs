using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Chat;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Chat.Tools;

/// <summary>
/// Contributes <c>get_document_outline</c> and <c>get_document_excerpt</c> AIFunctions to the
/// chat agent. These are the precise-navigation complement to <c>search_paperbase_documents</c>
/// (vector recall): outline returns the heading tree without body text; excerpt returns
/// exact-substring matches with surrounding context. Vector search misses these — semantic
/// embeddings are bad at precise tokens (contract numbers, IDs, dates, proper nouns).
///
/// <para>
/// Lives in the core tool stack alongside <see cref="DocumentRelationsTool"/> because the
/// <see cref="Document"/> aggregate is owned by Core; any business module's Chat session
/// can use these to drill into a candidate document returned by its own search tool.
/// </para>
/// <para>
/// fail-closed safety contract — see <c>.claude/rules/doc-chat-anti-patterns.md</c>
/// reverse example C: explicit <see cref="PaperbasePermissions.Documents.Default"/>
/// permission check, explicit tenant predicate (cross-tenant hits collapse to
/// <c>not_found</c> rather than leaking the document's existence), hard upper bounds
/// on header count (<see cref="MaxOutlineHeaders"/>) and excerpt match count
/// (<see cref="MaxExcerptMatches"/>), static-constant tool name and description.
/// </para>
/// </summary>
public class DocumentContentTool : ITransientDependency
{
    /// <summary>Max headers returned per outline call. Above this the structure is too noisy to be useful in-context anyway.</summary>
    public const int MaxOutlineHeaders = 50;

    /// <summary>Max excerpt windows returned per call. Lower than outline because each match expands to several context lines.</summary>
    public const int MaxExcerptMatches = 5;

    /// <summary>Lines of surrounding context per excerpt match (before + after).</summary>
    public const int ExcerptContextLines = 2;

    private readonly IDocumentRepository _repository;
    private readonly IAuthorizationService _authorizationService;

    public DocumentContentTool(
        IDocumentRepository repository,
        IAuthorizationService authorizationService)
    {
        _repository = repository;
        _authorizationService = authorizationService;
    }

    public virtual AIFunction CreateOutlineFunction(
        ChatToolContext ctx,
        IChatToolFactory toolFactory)
    {
        var binding = new OutlineBinding(_repository, _authorizationService, ctx.TenantId);
        return toolFactory.Create(
            ctx,
            binding.GetOutlineAsync,
            name: "get_document_outline",
            description:
                "Get the heading outline (table of contents) of a single document by its ID. " +
                "Returns header level (1-6) and title text only — no body text. " +
                "Use this when the user asks about a document's structure (\"how many sections\", " +
                "\"what chapters\", \"what's in section X\") instead of doing a full vector search. " +
                $"Returns up to {MaxOutlineHeaders} headers; documents with deeper structure are truncated.",
            progressDescriber: _ => "正在读取文档大纲…");
    }

    public virtual AIFunction CreateExcerptFunction(
        ChatToolContext ctx,
        IChatToolFactory toolFactory)
    {
        var binding = new ExcerptBinding(_repository, _authorizationService, ctx.TenantId);
        return toolFactory.Create(
            ctx,
            binding.GetExcerptAsync,
            name: "get_document_excerpt",
            description:
                "Search for an exact substring (case-insensitive) inside a single document's Markdown body " +
                "and return matching passages with " + ExcerptContextLines + " lines of context before and after each hit. " +
                "Use this for precise token lookups where vector search struggles: contract numbers, invoice IDs, " +
                "specific dates, proper nouns, or any literal phrase the user quotes. " +
                $"Returns up to {MaxExcerptMatches} matches; overlapping context windows are merged so consecutive hits " +
                "do not return duplicated lines.",
            progressDescriber: _ => "正在文档内精确检索…");
    }

    /// <summary>
    /// Holds the bound context for the <c>get_document_outline</c> AIFunction. Factored
    /// so parameter-level <see cref="DescriptionAttribute"/>s are accessible via reflection
    /// (lambda parameters cannot carry attributes in C#).
    /// </summary>
    private sealed class OutlineBinding
    {
        private readonly IDocumentRepository _repository;
        private readonly IAuthorizationService _authorizationService;
        private readonly Guid? _tenantId;

        public OutlineBinding(
            IDocumentRepository repository,
            IAuthorizationService authorizationService,
            Guid? tenantId)
        {
            _repository = repository;
            _authorizationService = authorizationService;
            _tenantId = tenantId;
        }

        public async Task<string> GetOutlineAsync(
            [Description("Document ID to read the outline from. Must be a document the caller has access to in the current tenant.")]
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            await _authorizationService.CheckAsync(PaperbasePermissions.Documents.Default);

            var document = await _repository.FindAsync(documentId, cancellationToken: cancellationToken);
            // Explicit tenant predicate — collapse cross-tenant hits to "not found" so
            // existence in other tenants is not leaked. Defense-in-depth against any
            // code path that disables ABP's ambient DataFilter (background jobs etc.).
            if (document is null || document.TenantId != _tenantId)
            {
                return JsonSerializer.Serialize(new { documentId, error = "not_found", count = 0, headers = Array.Empty<object>() });
            }

            var headers = MarkdownOutline.Extract(document.Markdown, maxHeaders: MaxOutlineHeaders);
            return JsonSerializer.Serialize(new
            {
                documentId,
                count = headers.Count,
                truncated = headers.Count >= MaxOutlineHeaders,
                headers
            });
        }
    }

    /// <summary>
    /// Holds the bound context for the <c>get_document_excerpt</c> AIFunction.
    /// </summary>
    private sealed class ExcerptBinding
    {
        private readonly IDocumentRepository _repository;
        private readonly IAuthorizationService _authorizationService;
        private readonly Guid? _tenantId;

        public ExcerptBinding(
            IDocumentRepository repository,
            IAuthorizationService authorizationService,
            Guid? tenantId)
        {
            _repository = repository;
            _authorizationService = authorizationService;
            _tenantId = tenantId;
        }

        public async Task<string> GetExcerptAsync(
            [Description("Document ID to search inside.")]
            Guid documentId,
            [Description("Exact substring or phrase to find (case-insensitive). Pass the literal token from the user's question — do not paraphrase.")]
            string query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonSerializer.Serialize(new { documentId, error = "empty_query", count = 0, matches = Array.Empty<string>() });
            }

            await _authorizationService.CheckAsync(PaperbasePermissions.Documents.Default);

            var document = await _repository.FindAsync(documentId, cancellationToken: cancellationToken);
            if (document is null || document.TenantId != _tenantId)
            {
                return JsonSerializer.Serialize(new { documentId, error = "not_found", count = 0, matches = Array.Empty<string>() });
            }

            var matches = MarkdownOutline.Grep(
                document.Markdown, query,
                contextLines: ExcerptContextLines,
                maxMatches: MaxExcerptMatches);

            return JsonSerializer.Serialize(new
            {
                documentId,
                count = matches.Count,
                truncated = matches.Count >= MaxExcerptMatches,
                matches
            });
        }
    }
}
