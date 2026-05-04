using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.KnowledgeIndex;
using Microsoft.Extensions.AI;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;
using MEAI = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

// ── Test module ───────────────────────────────────────────────────────────────

/// <summary>
/// Extends <see cref="DocumentChatAppServiceTestModule"/> with
/// <c>ChatSearchBehavior.OnDemandFunctionCalling</c> so that all services within
/// this test scope use the on-demand retrieval mode.
/// </summary>
[DependsOn(typeof(DocumentChatAppServiceTestModule))]
public class DocumentChatOnDemandTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<PaperbaseAIBehaviorOptions>(opts =>
            opts.ChatSearchBehavior = ChatSearchBehavior.OnDemandFunctionCalling);
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Behavioral tests for <see cref="ChatSearchBehavior.OnDemandFunctionCalling"/> mode.
/// Exercises: search is skipped when the model does not invoke the retrieval tool
/// (IsDegraded=true), the knowledge index is not called, and both sync and streaming
/// paths report degraded state correctly.
///
/// Companion tests for <see cref="ChatSearchBehavior.BeforeAIInvoke"/> (default) are
/// in <see cref="DocumentChatStreaming_Tests"/> and <see cref="DocumentChatAppService_Tests"/>.
/// </summary>
public class DocumentChatOnDemand_Tests
    : PaperbaseApplicationTestBase<DocumentChatOnDemandTestModule>
{
    private readonly IDocumentChatAppService _appService;
    private readonly IChatClient _chatClient;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    private static readonly Guid OwnerUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public DocumentChatOnDemand_Tests()
    {
        _appService         = GetRequiredService<IDocumentChatAppService>();
        _chatClient         = GetRequiredService<IChatClient>();
        _knowledgeIndex     = GetRequiredService<IDocumentKnowledgeIndex>();
        _embeddingGenerator = GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _principalAccessor  = GetRequiredService<ICurrentPrincipalAccessor>();

        SetupDefaultEmbedding();
        SetupDefaultKnowledgeIndex();
        SetupDefaultSyncChatClient();
        SetupDefaultStreamingChatClient();
    }

    // ── 1. Sync path: IsDegraded=true when model does not invoke search tool ──

    [Fact]
    public async Task SendMessageAsync_IsDegraded_True_When_Model_Does_Not_Call_Search_Tool()
    {
        var conversationId = await CreateConversationAsync();

        ChatTurnResultDto result = null!;
        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                result = await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message      = "What are the payment terms?",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        // The substituted IChatClient never invokes function tools, so the search
        // delegate is never triggered in OnDemand mode → LastResults == null → IsDegraded.
        result.IsDegraded.ShouldBeTrue();
    }

    // ── 2. Sync path: knowledge index is never called in OnDemand mode ────────

    [Fact]
    public async Task SendMessageAsync_KnowledgeIndex_Not_Called_In_OnDemand_Mode()
    {
        var conversationId = await CreateConversationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message      = "What are the payment terms?",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        // In OnDemandFunctionCalling mode the search delegate is exposed as a tool,
        // not called eagerly; since the substitute never calls the tool, SearchAsync
        // must never have been invoked.
        await _knowledgeIndex.DidNotReceive().SearchAsync(
            Arg.Any<VectorSearchRequest>(),
            Arg.Any<CancellationToken>());
    }

    // ── 3. Streaming path: Done.IsDegraded=true when model does not call tool ─

    [Fact]
    public async Task SendMessageStreamingAsync_IsDegraded_True_When_Model_Does_Not_Call_Search_Tool()
    {
        var conversationId = await CreateConversationAsync();

        ChatTurnDeltaDto? done = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                await foreach (var delta in _appService.SendMessageStreamingAsync(
                    conversationId,
                    new SendChatMessageInput { Message = "q", ClientTurnId = Guid.NewGuid() }))
                {
                    if (delta.Kind == ChatTurnDeltaKind.Done)
                        done = delta;
                }
            }
        });

        done.ShouldNotBeNull();
        done!.IsDegraded.ShouldBeTrue();
    }

    // ── 4. Streaming path: knowledge index not called in OnDemand mode ────────

    [Fact]
    public async Task SendMessageStreamingAsync_KnowledgeIndex_Not_Called_In_OnDemand_Mode()
    {
        var conversationId = await CreateConversationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                await foreach (var _ in _appService.SendMessageStreamingAsync(
                    conversationId,
                    new SendChatMessageInput { Message = "q", ClientTurnId = Guid.NewGuid() }))
                { }
            }
        });

        await _knowledgeIndex.DidNotReceive().SearchAsync(
            Arg.Any<VectorSearchRequest>(),
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateConversationAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                var dto = await _appService.CreateConversationAsync(new CreateChatConversationInput
                {
                    Title            = "OnDemand Test",
                    DocumentTypeCode = "contract.general"
                });
                return dto.Id;
            }
        });
    }

    private IDisposable ChangeUser(Guid userId)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId.ToString()) };
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }

    private void SetupDefaultEmbedding()
    {
        var embedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _embeddingGenerator
            .GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([embedding]));
    }

    private void SetupDefaultKnowledgeIndex()
    {
        _knowledgeIndex
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());
    }

    private void SetupDefaultSyncChatClient()
    {
        _chatClient.GetResponseAsync(
                Arg.Any<IEnumerable<MEAI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                new ChatResponse([new MEAI.ChatMessage(ChatRole.Assistant, "stub answer")])));
    }

    private void SetupDefaultStreamingChatClient()
    {
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<MEAI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => FakeStream(new[] { "stub ", "answer" }));
    }

    private static async IAsyncEnumerable<MEAI.ChatResponseUpdate> FakeStream(
        IEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return new MEAI.ChatResponseUpdate(MEAI.ChatRole.Assistant, chunk);
        }
    }
}
