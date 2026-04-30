using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Permissions;
using Dignite.Paperbase.Rag;
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

namespace Dignite.Paperbase.Documents.Chat;

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
    private readonly PaperbaseAIOptions _aiOptions;
    private readonly PaperbaseRagOptions _ragOptions;

    public DocumentChatAppService(
        IChatConversationRepository conversationRepository,
        IDocumentRepository documentRepository,
        DocumentTextSearchAdapter textSearchAdapter,
        IChatClient chatClient,
        IPromptProvider promptProvider,
        PaperbasePostgresChatHistoryProvider historyProvider,
        IOptions<PaperbaseAIOptions> aiOptions,
        IOptions<PaperbaseRagOptions> ragOptions)
    {
        _conversationRepository = conversationRepository;
        _documentRepository = documentRepository;
        _textSearchAdapter = textSearchAdapter;
        _chatClient = chatClient;
        _promptProvider = promptProvider;
        _historyProvider = historyProvider;
        _aiOptions = aiOptions.Value;
        _ragOptions = ragOptions.Value;
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
            IsDegraded = false
        };
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

    protected virtual async Task<AgentRunOutcome> InvokeAgentAsync(
        ChatConversation conversation,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Capture tenant + scope from the conversation aggregate, not from ICurrentTenant.
        // The closure inside TextSearchProvider must see the right values when MAF
        // dispatches the search delegate on a possibly-different thread.
        var scope = new DocumentSearchScope
        {
            DocumentId = conversation.DocumentId,
            DocumentTypeCode = conversation.DocumentTypeCode,
            TopK = conversation.TopK,
            MinScore = conversation.MinScore
        };
        var (provider, capture) = _textSearchAdapter.CreateForTenant(
            conversation.TenantId,
            providerOptions: null,
            scope: scope);

        var template = _promptProvider.GetQaPrompt(_aiOptions.DefaultLanguage);
        var instructions = template.SystemInstructions + " " + PromptBoundary.BoundaryRule;

        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = instructions },
            ChatHistoryProvider = _historyProvider,
            AIContextProviders = new List<AIContextProvider> { provider }
        };

        var agent = new ChatClientAgent(_chatClient, agentOptions);

        var session = await agent.CreateSessionAsync(cancellationToken);
        session.StateBag.SetValue(
            PaperbasePostgresChatHistoryProvider.ConversationIdStateKey,
            conversation.Id.ToString());

        var history = await _historyProvider.GetChatHistoryAsync(session, cancellationToken);
        var messages = history
            .Concat([new MeAi.ChatMessage(MeAi.ChatRole.User, message)])
            .ToList();

        var response = await agent.RunAsync(messages, session, options: null, cancellationToken);
        return new AgentRunOutcome(response.Text, capture);
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
    /// Applies a soft upper-bound (<see cref="DocumentChatConsts.MaxCitationsJsonLength"/>):
    /// if the serialized string is too long, trailing citations are dropped and a warning is logged.
    /// </summary>
    protected virtual string? SerializeCitations(IReadOnlyList<VectorSearchResult>? results)
    {
        if (results == null || results.Count == 0)
            return null;

        var dtos = BuildCitationDtos(results);
        var json = JsonSerializer.Serialize(dtos);

        if (json.Length <= DocumentChatConsts.MaxCitationsJsonLength)
            return json;

        Logger.LogWarning(
            "CitationsJson exceeds {Max} chars; truncating from {Count} citations",
            DocumentChatConsts.MaxCitationsJsonLength, dtos.Count);

        while (dtos.Count > 0 && json.Length > DocumentChatConsts.MaxCitationsJsonLength)
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

    protected record AgentRunOutcome(
        string Text,
        DocumentSearchCapture Capture);
}
