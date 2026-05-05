using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Chat;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Chat.Search;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Permissions;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using MeAi = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

[Authorize(PaperbasePermissions.Documents.Chat.Default)]
public class DocumentChatAppService : PaperbaseAppService, IDocumentChatAppService
{
    /// <summary>
    /// Tail window of messages the repository returns when loading a conversation.
    /// Bounds the database read; the agent's prompt payload is bounded separately by
    /// <c>TextSearchProviderOptions.RecentMessageMemoryLimit</c>.
    /// </summary>
    protected virtual int MaxHistoryMessages => 50;

    private const int SnippetMaxGraphemes = 200;

    private readonly IChatConversationRepository _conversationRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentTextSearchAdapter _textSearchAdapter;
    private readonly IChatClient _chatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly PaperbasePostgresChatHistoryProvider _historyProvider;
    private readonly PaperbaseAIBehaviorOptions _aiOptions;
    private readonly PaperbaseKnowledgeIndexOptions _ragOptions;
    private readonly IEnumerable<IDocumentChatToolContributor> _toolContributors;

    public DocumentChatAppService(
        IChatConversationRepository conversationRepository,
        IDocumentRepository documentRepository,
        DocumentTextSearchAdapter textSearchAdapter,
        IChatClient chatClient,
        IPromptProvider promptProvider,
        PaperbasePostgresChatHistoryProvider historyProvider,
        IOptions<PaperbaseAIBehaviorOptions> aiOptions,
        IOptions<PaperbaseKnowledgeIndexOptions> ragOptions,
        IEnumerable<IDocumentChatToolContributor> toolContributors)
    {
        _conversationRepository = conversationRepository;
        _documentRepository = documentRepository;
        _textSearchAdapter = textSearchAdapter;
        _chatClient = chatClient;
        _promptProvider = promptProvider;
        _historyProvider = historyProvider;
        _aiOptions = aiOptions.Value;
        _ragOptions = ragOptions.Value;
        _toolContributors = toolContributors;
    }

    [Authorize(PaperbasePermissions.Documents.Chat.Create)]
    public virtual async Task<ChatConversationDto> CreateConversationAsync(CreateChatConversationInput input)
    {
        // Mutual-exclusion of DocumentId and DocumentTypeCode is enforced by
        // CreateChatConversationInput.IValidatableObject.Validate via the
        // ValidationInterceptor, which runs before this method body.

        if (input.DocumentId.HasValue)
        {
            // Will throw EntityNotFoundException → 404 if missing or filtered out by tenant.
            await _documentRepository.GetAsync(input.DocumentId.Value);
        }

        var title = string.IsNullOrWhiteSpace(input.Title)
            ? L["DocumentChat:UntitledConversation"].Value
            : input.Title!;

        var conversation = new ChatConversation(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            title,
            input.DocumentId,
            string.IsNullOrWhiteSpace(input.DocumentTypeCode) ? null : input.DocumentTypeCode,
            input.TopK,
            input.MinScore);

        await _conversationRepository.InsertAsync(conversation, autoSave: true);
        return ObjectMapper.Map<ChatConversation, ChatConversationDto>(conversation);
    }

    public virtual async Task<PagedResultDto<ChatConversationListItemDto>> GetConversationListAsync(
        GetChatConversationListInput input)
    {
        // ABP's IMultiTenant data filter scopes this query to CurrentTenant automatically;
        // the explicit CreatorId filter restricts it to the caller's own conversations.
        var queryable = await _conversationRepository.GetQueryableAsync();
        var ownerId = CurrentUser.Id;
        queryable = queryable.Where(c => c.CreatorId == ownerId);

        var totalCount = await AsyncExecuter.LongCountAsync(queryable);
        var page = await AsyncExecuter.ToListAsync(
            queryable.OrderByDescending(c => c.CreationTime)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<ChatConversationListItemDto>(
            totalCount,
            ObjectMapper.Map<List<ChatConversation>, List<ChatConversationListItemDto>>(page));
    }

    public virtual async Task<ChatConversationDto> GetConversationAsync(Guid conversationId)
    {
        var conversation = await LoadAndAuthorizeAsync(conversationId);
        return ObjectMapper.Map<ChatConversation, ChatConversationDto>(conversation);
    }

    [Authorize(PaperbasePermissions.Documents.Chat.Delete)]
    public virtual async Task DeleteConversationAsync(Guid conversationId)
    {
        var conversation = await LoadAndAuthorizeAsync(conversationId);
        await _conversationRepository.DeleteAsync(conversation, autoSave: true);
    }

    public virtual async Task<PagedResultDto<ChatMessageDto>> GetMessageListAsync(
        Guid conversationId,
        GetChatMessageListInput input)
    {
        var conversation = await LoadAndAuthorizeAsync(conversationId, includeMessages: true);
        var totalCount = conversation.Messages.Count;
        var paged = conversation.Messages
            .OrderBy(m => m.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<ChatMessageDto>(
            totalCount,
            ObjectMapper.Map<List<ChatMessage>, List<ChatMessageDto>>(paged));
    }

    [Authorize(PaperbasePermissions.Documents.Chat.SendMessage)]
    public virtual async Task<ChatTurnResultDto> SendMessageAsync(
        Guid conversationId,
        SendChatMessageInput input)
    {
        var conversation = await LoadAndAuthorizeAsync(conversationId, includeMessages: true);

        // Idempotency short-circuit: if this ClientTurnId has already produced a turn,
        // rebuild the result from persisted rows (never from in-memory state).
        var existingUserMessage = conversation.Messages
            .FirstOrDefault(m => m.Role == ChatMessageRole.User && m.ClientTurnId == input.ClientTurnId);
        if (existingUserMessage != null)
        {
            return BuildTurnResultFromPersisted(conversation, existingUserMessage);
        }

        var run = await InvokeAgentAsync(conversation, input.Message);

        var userMessageId = GuidGenerator.Create();
        var assistantMessageId = GuidGenerator.Create();

        conversation.AppendUserMessage(Clock, userMessageId, input.Message, input.ClientTurnId);

        var citationsJson = SerializeCitations(run.Capture.LastResults);
        conversation.AppendAssistantMessage(Clock, assistantMessageId, run.Text, citationsJson);

        // The aggregate is already tracked through the FindByIdWithMessagesAsync load;
        // the ambient unit of work flushes changes on commit. Calling repository.UpdateAsync
        // on a tracked entity would route through DbContext.Update(), which can clobber
        // ConcurrencyStamp original values. ABP's UoW commits via SaveChanges; concurrency
        // mismatch surfaces as AbpDbConcurrencyException → 409 (mapping handled by ABP).

        var citations = BuildCitationDtos(run.Capture.LastResults);
        return new ChatTurnResultDto
        {
            UserMessageId = userMessageId,
            AssistantMessageId = assistantMessageId,
            Answer = run.Text,
            Citations = citations,
            // null LastResults means the model did not invoke the search tool (OnDemand mode).
            IsDegraded = run.Capture.LastResults == null
        };
    }

    [Authorize(PaperbasePermissions.Documents.Chat.SendMessage)]
    public virtual async IAsyncEnumerable<ChatTurnDeltaDto> SendMessageStreamingAsync(
        Guid conversationId,
        SendChatMessageInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = await LoadAndAuthorizeAsync(conversationId, includeMessages: true, cancellationToken);

        // Idempotency short-circuit: replay as a single Done event without re-invoking the model.
        var existingUserMessage = conversation.Messages
            .FirstOrDefault(m => m.Role == ChatMessageRole.User && m.ClientTurnId == input.ClientTurnId);
        if (existingUserMessage != null)
        {
            var priorResult = BuildTurnResultFromPersisted(conversation, existingUserMessage);
            yield return new ChatTurnDeltaDto
            {
                Kind = ChatTurnDeltaKind.Done,
                UserMessageId = priorResult.UserMessageId,
                AssistantMessageId = priorResult.AssistantMessageId,
                Citations = priorResult.Citations
            };
            yield break;
        }

        // Use a channel to bridge the producer (agent streaming + persistence) with this
        // async iterator. This allows full try/catch inside the producer without violating
        // the C# restriction on yield-inside-catch, while still delivering incremental
        // deltas to the consumer as they arrive from the LLM.
        var channel = Channel.CreateUnbounded<ChatTurnDeltaDto>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        var fillTask = FillStreamingChannelAsync(conversation, input, channel.Writer, cancellationToken);

        await foreach (var delta in channel.Reader.ReadAllAsync(cancellationToken))
            yield return delta;

        // fillTask always completes before ReadAllAsync (the writer calls Complete() before
        // returning). Await to surface any unexpected unhandled exception.
        try { await fillTask; }
        catch { /* error event was already written to the channel */ }
    }


    /// <summary>
    /// Loads the conversation aggregate and runs the fail-closed authorization gate.
    /// Order: permission attribute (ABP) → tenant assertion → ownership assertion.
    /// Any mismatch returns <see cref="EntityNotFoundException"/> (404 from ABP) rather
    /// than AuthorizationException (403) to avoid disclosing existence.
    /// </summary>
    protected virtual async Task<ChatConversation> LoadAndAuthorizeAsync(
        Guid conversationId,
        bool includeMessages = false,
        CancellationToken cancellationToken = default)
    {
        var conversation = includeMessages
            ? await _conversationRepository.FindByIdWithMessagesAsync(
                conversationId, MaxHistoryMessages, cancellationToken)
            : await _conversationRepository.FindAsync(conversationId, cancellationToken: cancellationToken);

        if (conversation is null)
        {
            throw new EntityNotFoundException(typeof(ChatConversation), conversationId);
        }

        if (conversation.TenantId != CurrentTenant.Id)
        {
            Logger.LogWarning(
                "doc-chat tenant mismatch: ConversationId={ConversationId} ConversationTenant={ConvTenant} CurrentTenant={CurrentTenant} CurrentUser={UserId}",
                conversationId, conversation.TenantId, CurrentTenant.Id, CurrentUser.Id);
            throw new EntityNotFoundException(typeof(ChatConversation), conversationId);
        }

        if (conversation.CreatorId != CurrentUser.Id)
        {
            Logger.LogWarning(
                "doc-chat ownership mismatch: ConversationId={ConversationId} Owner={OwnerId} CurrentUser={UserId}",
                conversationId, conversation.CreatorId, CurrentUser.Id);
            throw new EntityNotFoundException(typeof(ChatConversation), conversationId);
        }

        return conversation;
    }

    /// <summary>
    /// Prepares the MAF agent and a fresh <see cref="AgentSession"/> for the given
    /// conversation. Shared by both the synchronous and streaming paths.
    ///
    /// <para>
    /// Security invariant: <c>conversation.TenantId</c> and <c>conversation.DocumentTypeCode</c>
    /// are read from the aggregate (loaded and authorized in <see cref="LoadAndAuthorizeAsync"/>),
    /// never from <c>ICurrentTenant</c>. This preserves the fail-closed tenant assertion
    /// in all code paths.
    /// </para>
    /// </summary>
    protected virtual async Task<AgentSetup> PrepareAgentSetupAsync(
        ChatConversation conversation,
        CancellationToken cancellationToken = default)
    {
        // Scope is built from the aggregate, not from ambient context — required so the
        // search delegate closure captures the right tenant even on background threads.
        var scope = new DocumentSearchScope
        {
            DocumentId = conversation.DocumentId,
            DocumentTypeCode = conversation.DocumentTypeCode,
            TopK = conversation.TopK,
            MinScore = conversation.MinScore
        };

        var template = _promptProvider.GetQaPrompt(_aiOptions.DefaultLanguage);
        var instructions = template.SystemInstructions + " " + PromptBoundary.BoundaryRule;

        // Collect AIFunction tools from all contributors registered for this document type.
        var contributorTools = CollectContributorTools(conversation);

        // When contributors are present, the model must be able to decide when to call the RAG
        // tool (e.g. chain: search_contracts → search_paperbase_documents).  BeforeAIInvoke
        // would force vector search unconditionally, which wastes tokens for structured queries
        // and prevents the model from skipping it when the contributor already answered fully.
        var effectiveBehavior = contributorTools.Count > 0
            ? ChatSearchBehavior.OnDemandFunctionCalling
            : _aiOptions.ChatSearchBehavior;

        ChatClientAgentOptions agentOptions;
        DocumentSearchCapture capture;

        if (effectiveBehavior == ChatSearchBehavior.OnDemandFunctionCalling)
        {
            // OnDemandFunctionCalling: expose both the RAG search function and contributor
            // tools as LLM-callable functions.  The search function accepts an optional
            // documentIds parameter so the LLM can do a focused RAG pass on document IDs
            // returned by contributor tools (e.g. search_contracts → search_paperbase_documents).
            //
            // Security note: function name and description are static string literals —
            // they MUST NOT contain user input or conversation metadata (prompt-injection risk).
            capture = new DocumentSearchCapture();
            var searchFn = _textSearchAdapter.CreateSearchFunction(
                conversation.TenantId,
                scope,
                capture,
                functionName: "search_paperbase_documents",
                functionDescription:
                    "Search Paperbase documents within the conversation's scope. " +
                    "Returns relevant text chunks with source citations. " +
                    "Pass documentIds to restrict the search to specific documents " +
                    "whose IDs were returned by another tool.");

            var allTools = new List<AITool> { searchFn };
            allTools.AddRange(contributorTools);

            agentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = allTools
                },
                ChatHistoryProvider = _historyProvider
            };
        }
        else
        {
            // BeforeAIInvoke: TextSearchProvider injects RAG context automatically before
            // each AI invocation; citations are always populated.  Contributor tools (if any)
            // are still exposed as LLM-callable functions so iterative tool calling works.
            var (provider, providerCapture) = _textSearchAdapter.CreateForTenant(
                conversation.TenantId,
                providerOptions: BuildChatSearchProviderOptions(),
                scope: scope);
            capture = providerCapture;

            agentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = contributorTools.Count > 0 ? contributorTools : null
                },
                ChatHistoryProvider = _historyProvider,
                AIContextProviders = new List<AIContextProvider> { provider }
            };
        }

        var agent = new ChatClientAgent(_chatClient, agentOptions);
        var session = await agent.CreateSessionAsync(cancellationToken);
        session.StateBag.SetValue(
            PaperbasePostgresChatHistoryProvider.ConversationIdStateKey,
            conversation.Id.ToString());

        return new AgentSetup(agent, session, capture);
    }

    /// <summary>
    /// Collects <see cref="AIFunction"/> tools from all <see cref="IDocumentChatToolContributor"/>
    /// implementations whose <see cref="IDocumentChatToolContributor.DocumentTypeCode"/> matches
    /// the conversation's document type.  Returns an empty list when no contributors match.
    /// </summary>
    protected virtual List<AITool> CollectContributorTools(ChatConversation conversation)
    {
        if (!_toolContributors.Any())
            return new List<AITool>();

        var ctx = new DocumentChatToolContext
        {
            DocumentTypeCode = conversation.DocumentTypeCode,
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id
        };

        return _toolContributors
            .Where(c => c.DocumentTypeCode == conversation.DocumentTypeCode)
            .SelectMany(c => c.ContributeTools(ctx))
            .Cast<AITool>()
            .ToList();
    }

    /// <summary>
    /// Returns <see cref="TextSearchProviderOptions"/> for the <c>BeforeAIInvoke</c> path.
    /// Returns <see langword="null"/> so the adapter applies its own defaults
    /// (<c>RecentMessageMemoryLimit = 5</c>, <c>BeforeAIInvoke</c> search time).
    /// Override in a subclass to customize retrieval behaviour without touching
    /// <see cref="PrepareAgentSetupAsync"/>.
    /// </summary>
    protected virtual TextSearchProviderOptions? BuildChatSearchProviderOptions() => null;

    protected virtual async Task<AgentRunOutcome> InvokeAgentAsync(
        ChatConversation conversation,
        string message,
        CancellationToken cancellationToken = default)
    {
        var setup = await PrepareAgentSetupAsync(conversation, cancellationToken);
        var history = await _historyProvider.GetChatHistoryAsync(setup.Session, cancellationToken);
        var messages = history
            .Concat([new MeAi.ChatMessage(MeAi.ChatRole.User, message)])
            .ToList();
        var response = await setup.Agent.RunAsync(messages, setup.Session, options: null, cancellationToken);
        return new AgentRunOutcome(response.Text, setup.Capture);
    }

    protected virtual ChatTurnResultDto BuildTurnResultFromPersisted(
        ChatConversation conversation,
        ChatMessage userMessage)
    {
        // Find the assistant message that follows this user turn (idempotent replay
        // must reflect the same persisted result the original turn produced).
        var assistantMessage = conversation.Messages
            .Where(m => m.Role == ChatMessageRole.Assistant
                && m.CreationTime >= userMessage.CreationTime)
            .OrderBy(m => m.CreationTime)
            .FirstOrDefault();

        var citations = string.IsNullOrEmpty(assistantMessage?.CitationsJson)
            ? new List<ChatCitationDto>()
            : (DeserializeCitations(assistantMessage!.CitationsJson!) ?? new List<ChatCitationDto>());

        return new ChatTurnResultDto
        {
            UserMessageId = userMessage.Id,
            AssistantMessageId = assistantMessage?.Id ?? Guid.Empty,
            Answer = assistantMessage?.Content ?? string.Empty,
            Citations = citations,
            IsDegraded = false
        };
    }

    protected virtual List<ChatCitationDto>? DeserializeCitations(string citationsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ChatCitationDto>>(citationsJson);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to deserialize CitationsJson; returning empty list.");
            return null;
        }
    }

    /// <summary>
    /// Serializes <paramref name="results"/> to JSON for persistence.
    /// Applies a soft upper-bound (<see cref="ChatConsts.MaxCitationsJsonLength"/>):
    /// if the serialized string is too long, trailing citations are dropped and a warning is logged.
    /// </summary>
    protected virtual string? SerializeCitations(IReadOnlyList<VectorSearchResult>? results)
    {
        if (results == null || results.Count == 0)
            return null;

        var dtos = BuildCitationDtos(results);
        var json = JsonSerializer.Serialize(dtos);

        if (json.Length <= ChatConsts.MaxCitationsJsonLength)
            return json;

        Logger.LogWarning(
            "CitationsJson exceeds {Max} chars; truncating from {Count} citations",
            ChatConsts.MaxCitationsJsonLength, dtos.Count);

        while (dtos.Count > 0 && json.Length > ChatConsts.MaxCitationsJsonLength)
        {
            dtos.RemoveAt(dtos.Count - 1);
            json = JsonSerializer.Serialize(dtos);
        }

        return dtos.Count > 0 ? json : null;
    }

    protected virtual List<ChatCitationDto> BuildCitationDtos(IReadOnlyList<VectorSearchResult>? results)
    {
        if (results == null)
            return new List<ChatCitationDto>();

        return results.Select(r => new ChatCitationDto
        {
            DocumentId = r.DocumentId,
            PageNumber = r.PageNumber,
            ChunkIndex = r.ChunkIndex,
            Snippet = TruncateByGrapheme(r.Text, SnippetMaxGraphemes),
            SourceName = r.PageNumber.HasValue
                ? $"Document {r.DocumentId} (page {r.PageNumber})"
                : $"Document {r.DocumentId} (chunk #{r.ChunkIndex})"
        }).ToList();
    }

    /// <summary>
    /// Runs the agent in streaming mode and writes <see cref="ChatTurnDeltaDto"/> events
    /// to <paramref name="writer"/>. Persists the full turn only on successful completion.
    /// On cancellation, the partial text is discarded and a warning is logged.
    /// On error, a <see cref="ChatTurnDeltaKind.Error"/> event is written before closing
    /// the channel.
    /// </summary>
    private async Task FillStreamingChannelAsync(
        ChatConversation conversation,
        SendChatMessageInput input,
        ChannelWriter<ChatTurnDeltaDto> writer,
        CancellationToken ct)
    {
        var userMessageId = GuidGenerator.Create();
        var assistantMessageId = GuidGenerator.Create();
        var sb = new StringBuilder();

        try
        {
            var setup = await PrepareAgentSetupAsync(conversation, ct);
            var history = await _historyProvider.GetChatHistoryAsync(setup.Session, ct);
            var messages = history
                .Concat([new MeAi.ChatMessage(MeAi.ChatRole.User, input.Message)])
                .ToList();

            await foreach (var update in setup.Agent.RunStreamingAsync(
                messages, setup.Session, options: null, ct))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(text);
                    await writer.WriteAsync(
                        new ChatTurnDeltaDto { Kind = ChatTurnDeltaKind.PartialText, Text = text },
                        ct);
                }
            }

            // Stream completed — persist the full turn in one shot.
            var fullText = sb.ToString();
            conversation.AppendUserMessage(Clock, userMessageId, input.Message, input.ClientTurnId);
            var citationsJson = SerializeCitations(setup.Capture.LastResults);
            conversation.AppendAssistantMessage(Clock, assistantMessageId, fullText, citationsJson);

            var citations = BuildCitationDtos(setup.Capture.LastResults);

            await writer.WriteAsync(new ChatTurnDeltaDto
            {
                Kind = ChatTurnDeltaKind.Done,
                UserMessageId = userMessageId,
                AssistantMessageId = assistantMessageId,
                Citations = citations,
                // null LastResults → model did not invoke the search tool (OnDemand mode).
                IsDegraded = setup.Capture.LastResults == null
            }, ct);

            writer.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected or request timed out. Partial text is discarded — we do
            // not persist an incomplete assistant turn to avoid confusing the idempotency
            // key logic on the next retry.
            Logger.LogWarning(
                "doc-chat streaming cancelled: ConversationId={ConversationId}; {Length} chars discarded.",
                conversation.Id, sb.Length);
            writer.Complete();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "doc-chat streaming error: ConversationId={ConversationId}", conversation.Id);

            // Write a safe error event (never expose internal exception details to the client).
            writer.TryWrite(new ChatTurnDeltaDto
            {
                Kind = ChatTurnDeltaKind.Error,
                ErrorMessage = L["DocumentChat:StreamError"].Value
            });
            writer.Complete();
        }
    }

    private static string TruncateByGrapheme(string text, int maxGraphemes)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var sb = new StringBuilder();
        var count = 0;
        while (enumerator.MoveNext() && count < maxGraphemes)
        {
            sb.Append((string)enumerator.Current);
            count++;
        }
        return sb.ToString();
    }

    // ── nested types ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fully-prepared agent ready to run a turn. Shared between sync and streaming paths.
    /// </summary>
    protected record AgentSetup(
        ChatClientAgent Agent,
        AgentSession Session,
        DocumentSearchCapture Capture);

    protected record AgentRunOutcome(
        string Text,
        DocumentSearchCapture Capture);
}
