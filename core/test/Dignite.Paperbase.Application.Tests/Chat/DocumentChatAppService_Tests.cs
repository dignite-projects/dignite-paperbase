using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.AI;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Validation;
using Xunit;
using MEAI = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Documents.Chat;

/// <summary>
/// Behavioral tests for <see cref="DocumentChatAppService"/>. Exercises:
/// fail-closed authorization (tenant + ownership), per-turn idempotency,
/// optimistic-concurrency surfacing, and search-scope propagation through the
/// MAF <c>TextSearchProvider</c> pipeline.
/// </summary>
public class DocumentChatAppService_Tests
    : PaperbaseApplicationTestBase<DocumentChatAppServiceTestModule>
{
    private readonly IDocumentChatAppService _appService;
    private readonly IChatConversationRepository _repository;
    private readonly IChatClient _chatClient;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ICurrentTenant _currentTenant;
    private readonly Volo.Abp.Timing.IClock _clock;

    private static readonly Guid OwnerUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public DocumentChatAppService_Tests()
    {
        _appService = GetRequiredService<IDocumentChatAppService>();
        _repository = GetRequiredService<IChatConversationRepository>();
        _chatClient = GetRequiredService<IChatClient>();
        _knowledgeIndex = GetRequiredService<IDocumentKnowledgeIndex>();
        _embeddingGenerator = GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _clock = GetRequiredService<Volo.Abp.Timing.IClock>();

        SetupDefaultEmbedding();
        SetupDefaultChatClient();
        SetupDefaultKnowledgeIndex();
    }

    // ── 1. CreateConversation: happy path ────────────────────────────────────

    [Fact]
    public async Task Should_Create_Conversation_When_Input_Is_Valid()
    {
        var dto = await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                return await _appService.CreateConversationAsync(new CreateChatConversationInput
                {
                    Title = "My contract",
                    DocumentTypeCode = "contract.general",
                    TopK = 5
                });
            }
        });

        dto.ShouldNotBeNull();
        dto.Title.ShouldBe("My contract");
        dto.DocumentTypeCode.ShouldBe("contract.general");
        dto.DocumentId.ShouldBeNull();
        dto.TopK.ShouldBe(5);
    }

    // ── 2. CreateConversation: scope conflict ────────────────────────────────

    [Fact]
    public async Task Should_Reject_When_Both_DocumentId_And_DocumentTypeCode_Provided()
    {
        await Should.ThrowAsync<AbpValidationException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                using (ChangeUser(OwnerUserId))
                {
                    await _appService.CreateConversationAsync(new CreateChatConversationInput
                    {
                        Title = "Bad",
                        DocumentId = Guid.NewGuid(),
                        DocumentTypeCode = "contract.general"
                    });
                }
            });
        });
    }

    // ── 3. SendMessage: history is carried across turns ─────────────────────

    [Fact]
    public async Task Should_Send_Multi_Turn_Messages_And_Carry_History()
    {
        var conversationId = await CreateConversationAsync();

        // Capture every IChatClient call so we can inspect the history payload of each turn.
        var capturedTurns = new List<List<MEAI.ChatMessage>>();
        _chatClient.GetResponseAsync(
                Arg.Do<IEnumerable<MEAI.ChatMessage>>(msgs =>
                    capturedTurns.Add(msgs.ToList())),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var n = capturedTurns.Count; // 1-indexed turn marker
                return Task.FromResult(
                    new ChatResponse([new MEAI.ChatMessage(ChatRole.Assistant, $"answer-{n}")]));
            });

        for (var i = 1; i <= 3; i++)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                using (ChangeUser(OwnerUserId))
                {
                    await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                    {
                        Message = $"q-{i}",
                        ClientTurnId = Guid.NewGuid()
                    });
                }
            });
        }

        capturedTurns.Count.ShouldBe(3);
        capturedTurns[0].Count.ShouldBe(1);
        capturedTurns[1].Count.ShouldBe(3);
        capturedTurns[2].Count.ShouldBe(5);
        capturedTurns[0].Select(m => m.Text).ShouldContain("q-1");
        capturedTurns[1].Select(m => m.Text).ShouldContain("q-1");
        capturedTurns[1].Select(m => m.Text).ShouldContain("answer-1");
        capturedTurns[1].Select(m => m.Text).ShouldContain("q-2");
        capturedTurns[2].Select(m => m.Text).ShouldContain("q-1");
        capturedTurns[2].Select(m => m.Text).ShouldContain("answer-1");
        capturedTurns[2].Select(m => m.Text).ShouldContain("q-2");
        capturedTurns[2].Select(m => m.Text).ShouldContain("answer-2");
        capturedTurns[2].Select(m => m.Text).ShouldContain("q-3");

        var conv = await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                return await _repository.FindByIdWithMessagesAsync(conversationId, 50);
            }
        });
        conv!.Messages.Count.ShouldBe(6);
    }

    // ── 4. SendMessage: scope propagated to vector search ───────────────────

    [Fact]
    public async Task Should_Pass_Scope_To_VectorSearchRequest()
    {
        var conversationId = await CreateConversationAsync(documentTypeCode: "contract.general", topK: 7);

        VectorSearchRequest? capturedRequest = null;
        _knowledgeIndex.SearchAsync(
                Arg.Do<VectorSearchRequest>(r => capturedRequest = r),
                Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        using (ChangeUser(OwnerUserId))
        {
            await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
            {
                Message = "What are the payment terms?",
                ClientTurnId = Guid.NewGuid()
            });
        }

        capturedRequest.ShouldNotBeNull();
        capturedRequest!.DocumentTypeCode.ShouldBe("contract.general");
        capturedRequest.TopK.ShouldBe(7);
    }

    // ── 5. Cross-tenant: 404 ────────────────────────────────────────────────

    [Fact]
    public async Task Should_Return_404_For_Cross_Tenant_Access()
    {
        var conversationId = await CreateConversationAsync();

        var otherTenantId = Guid.NewGuid();
        await Should.ThrowAsync<EntityNotFoundException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                // Same user, different tenant — fail-closed gate must reject.
                using (ChangeUser(OwnerUserId))
                using (_currentTenant.Change(otherTenantId))
                {
                    await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                    {
                        Message = "leak attempt",
                        ClientTurnId = Guid.NewGuid()
                    });
                }
            });
        });
    }

    // ── 6. Non-owner: 404 ───────────────────────────────────────────────────

    [Fact]
    public async Task Should_Return_404_For_Non_Owner()
    {
        var conversationId = await CreateConversationAsync();

        await Should.ThrowAsync<EntityNotFoundException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                using (ChangeUser(OtherUserId))
                {
                    await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                    {
                        Message = "leak attempt",
                        ClientTurnId = Guid.NewGuid()
                    });
                }
            });
        });
    }

    // ── 7. Idempotency: same ClientTurnId returns same result, model called once ──

    [Fact]
    public async Task Should_Be_Idempotent_For_Same_ClientTurnId()
    {
        var conversationId = await CreateConversationAsync();
        var clientTurnId = Guid.NewGuid();

        var first = await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                return await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "q",
                    ClientTurnId = clientTurnId
                });
            }
        });

        var second = await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                return await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "q",
                    ClientTurnId = clientTurnId
                });
            }
        });

        // Replay returns the persisted ids — never mints new ones.
        second.UserMessageId.ShouldBe(first.UserMessageId);
        second.AssistantMessageId.ShouldBe(first.AssistantMessageId);

        // Model must have been invoked exactly once across both posts.
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<MEAI.ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 8. Concurrency conflict surfaces as AbpDbConcurrencyException ───────

    [Fact]
    public async Task Should_Reject_Concurrent_Sends_With_409()
    {
        var conversationId = await CreateConversationAsync();

        // Read the aggregate on a separate UoW so we can hold a stale copy after a
        // competing turn rotates the row's ConcurrencyStamp.
        ChatConversation staleAggregate = null!;
        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                staleAggregate = (await _repository.FindByIdWithMessagesAsync(conversationId, 50))!;
            }
        });

        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "first",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        // Updating the stale aggregate must surface AbpDbConcurrencyException, which
        // ABP HTTP layer maps to 409 Conflict; the AppService must NOT catch it.
        await Should.ThrowAsync<AbpDbConcurrencyException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                using (ChangeUser(OwnerUserId))
                {
                    staleAggregate.AppendUserMessage(_clock, Guid.NewGuid(), "stale racer", Guid.NewGuid());
                    await _repository.UpdateAsync(staleAggregate, autoSave: true);
                }
            });
        });
    }

    // ── 9. Citations reflect injected chunks ─────────────────────────────────

    [Fact]
    public async Task Citations_Reflect_Injected_Chunks()
    {
        // 配置知识库返回 3 条带有确定性元数据的 chunk。
        var docId = Guid.NewGuid();
        var fakeResults = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 0, PageNumber = 1, Text = "chunk 0 text" },
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 1, PageNumber = 2, Text = "chunk 1 text" },
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 2, PageNumber = null, Text = "chunk 2 text" }
        };
        _knowledgeIndex.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(fakeResults);

        var conversationId = await CreateConversationAsync();
        ChatTurnResultDto result = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                result = await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "payment terms?",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        // DTO citations must align with what the knowledge index returned.
        result.Citations.Count.ShouldBe(3);
        for (var i = 0; i < 3; i++)
        {
            result.Citations[i].DocumentId.ShouldBe(docId);
            result.Citations[i].ChunkIndex.ShouldBe(i);
            result.Citations[i].PageNumber.ShouldBe(fakeResults[i].PageNumber);
            result.Citations[i].Snippet.ShouldBe(fakeResults[i].Text);
            result.Citations[i].SourceName.ShouldNotBeNullOrEmpty();
        }

        // Persisted CitationsJson must be parseable and contain the same 3 entries.
        var conv = await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                return await _repository.FindByIdWithMessagesAsync(conversationId, 50);
            }
        });

        var assistantMsg = conv!.Messages
            .Where(m => m.Role == ChatMessageRole.Assistant)
            .OrderBy(m => m.CreationTime)
            .First();

        assistantMsg.CitationsJson.ShouldNotBeNullOrEmpty();

        var persisted = System.Text.Json.JsonSerializer.Deserialize<List<ChatCitationDto>>(
            assistantMsg.CitationsJson!);
        persisted.ShouldNotBeNull();
        persisted!.Count.ShouldBe(3);
        persisted[0].DocumentId.ShouldBe(docId);
        persisted[0].PageNumber.ShouldBe(1);
        persisted[2].PageNumber.ShouldBeNull();
    }

    // ── 10. Snippet does not break multibyte characters ───────────────────────

    [Fact]
    public async Task Snippet_Does_Not_Break_Multibyte_Characters()
    {
        // 构造一段包含中日文字符 + emoji 的文本，头部超过 200 grapheme。
        // 断言截断后 JSON 序列化不抛异常，且字符无错位。
        var longText = string.Concat(Enumerable.Repeat("日本語テスト🚀", 30)); // ~240 graphemes
        var docId = Guid.NewGuid();
        _knowledgeIndex.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>
            {
                new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 0, Text = longText }
            });

        var conversationId = await CreateConversationAsync();
        ChatTurnResultDto result = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                result = await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "q",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        result.Citations.Count.ShouldBe(1);
        var snippet = result.Citations[0].Snippet;

        // Snippet must be at most 200 grapheme clusters (not 200 chars/bytes).
        var graphemeCount = new System.Globalization.StringInfo(snippet).LengthInTextElements;
        graphemeCount.ShouldBeLessThanOrEqualTo(200);

        // Must round-trip through JSON without error.
        var json = System.Text.Json.JsonSerializer.Serialize(result.Citations);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<List<ChatCitationDto>>(json);
        roundTripped.ShouldNotBeNull();
        roundTripped![0].Snippet.ShouldBe(snippet);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateConversationAsync(
        string? documentTypeCode = "contract.general",
        int? topK = null)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                var dto = await _appService.CreateConversationAsync(new CreateChatConversationInput
                {
                    Title = "Test",
                    DocumentTypeCode = documentTypeCode,
                    TopK = topK
                });
                return dto.Id;
            }
        });
    }

    private IDisposable ChangeUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, userId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return _principalAccessor.Change(principal);
    }

    private void SetupDefaultEmbedding()
    {
        var vector = new float[] { 0.1f, 0.2f, 0.3f };
        var embedding = new Embedding<float>(vector);
        var embeddings = new GeneratedEmbeddings<Embedding<float>>([embedding]);

        _embeddingGenerator
            .GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(embeddings);
    }

    private void SetupDefaultChatClient()
    {
        _chatClient.GetService(Arg.Any<Type>(), Arg.Any<object?>()).Returns(null);

        _chatClient.GetResponseAsync(
                Arg.Any<IEnumerable<MEAI.ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                new ChatResponse([new MEAI.ChatMessage(ChatRole.Assistant, "stub answer")])));
    }

    private void SetupDefaultKnowledgeIndex()
    {
        _knowledgeIndex
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());
    }
}
