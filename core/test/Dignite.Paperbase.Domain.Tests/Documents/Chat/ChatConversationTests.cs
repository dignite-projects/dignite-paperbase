using System;
using Dignite.Paperbase.Documents.Chat;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Paperbase.Documents.Chat;

public class ChatConversationTests
{
    private static IClock CreateClock(DateTime? at = null)
    {
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(at ?? new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static ChatConversation CreateConversation(
        Guid? documentId = null,
        string? documentTypeCode = null)
    {
        return new ChatConversation(
            Guid.NewGuid(),
            tenantId: null,
            title: "Test Conversation",
            documentId: documentId,
            documentTypeCode: documentTypeCode,
            topK: null,
            minScore: null);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 1. DocumentId + DocumentTypeCode 互斥校验
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Should_Throw_When_Both_DocumentId_And_TypeCode_Are_Set()
    {
        var ex = Should.Throw<BusinessException>(() =>
        {
            _ = new ChatConversation(
                Guid.NewGuid(),
                tenantId: null,
                title: "Bad",
                documentId: Guid.NewGuid(),
                documentTypeCode: "contract.general",
                topK: null,
                minScore: null);
        });

        ex.Code.ShouldBe(PaperbaseErrorCodes.ChatConversationScopeConflict);
    }

    [Fact]
    public void Constructor_Should_Accept_DocumentId_Only()
    {
        var conv = CreateConversation(documentId: Guid.NewGuid());
        conv.DocumentId.ShouldNotBeNull();
        conv.DocumentTypeCode.ShouldBeNull();
    }

    [Fact]
    public void Constructor_Should_Accept_TypeCode_Only()
    {
        var conv = CreateConversation(documentTypeCode: "contract.general");
        conv.DocumentTypeCode.ShouldBe("contract.general");
        conv.DocumentId.ShouldBeNull();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 2. Title 长度超限
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Should_Throw_When_Title_Exceeds_MaxLength()
    {
        Should.Throw<Exception>(() =>
        {
            _ = new ChatConversation(
                Guid.NewGuid(),
                tenantId: null,
                title: new string('x', DocumentChatConsts.MaxTitleLength + 1),
                documentId: null,
                documentTypeCode: null,
                topK: null,
                minScore: null);
        });
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 3. TenantId 不可变（无 public setter，无 Update 方法）
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TenantId_Should_Be_Immutable_After_Construction()
    {
        var tenantId = Guid.NewGuid();
        var conv = new ChatConversation(
            Guid.NewGuid(),
            tenantId: tenantId,
            title: "My Conversation",
            documentId: null,
            documentTypeCode: null,
            topK: null,
            minScore: null);

        conv.TenantId.ShouldBe(tenantId);

        // Assert TenantId is unchanged after mutations.
        var clock = CreateClock();
        conv.Rename("New Title");
        conv.AppendUserMessage(clock, Guid.NewGuid(), "Hello", Guid.NewGuid());

        conv.TenantId.ShouldBe(tenantId);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 4. AppendUserMessage 旋转 ConcurrencyStamp
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppendUserMessage_Should_Rotate_ConcurrencyStamp()
    {
        var conv = CreateConversation();
        var stampBefore = conv.ConcurrencyStamp;

        conv.AppendUserMessage(CreateClock(), Guid.NewGuid(), "Hello world", Guid.NewGuid());

        conv.ConcurrencyStamp.ShouldNotBe(stampBefore);
        conv.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 5. AppendUserMessage 重复 ClientTurnId → BusinessException
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppendUserMessage_Should_Throw_On_Duplicate_ClientTurnId()
    {
        var conv = CreateConversation();
        var clock = CreateClock();
        var clientTurnId = Guid.NewGuid();

        conv.AppendUserMessage(clock, Guid.NewGuid(), "First message", clientTurnId);

        var ex = Should.Throw<BusinessException>(() =>
        {
            conv.AppendUserMessage(clock, Guid.NewGuid(), "Duplicate", clientTurnId);
        });

        ex.Code.ShouldBe(PaperbaseErrorCodes.DuplicateClientTurnId);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 6. AppendAssistantMessage 不需要 ClientTurnId（null），多条不冲突
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppendAssistantMessage_Should_Not_Require_ClientTurnId()
    {
        var conv = CreateConversation();
        var clock = CreateClock();

        var msg1 = conv.AppendAssistantMessage(clock, Guid.NewGuid(), "Answer 1", citationsJson: null);
        var msg2 = conv.AppendAssistantMessage(clock, Guid.NewGuid(), "Answer 2", citationsJson: "[]");

        msg1.ClientTurnId.ShouldBeNull();
        msg2.ClientTurnId.ShouldBeNull();
        conv.Messages.Count.ShouldBe(2);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 7. Rename 旋转 ConcurrencyStamp；UpdateAgentSession 旋转 ConcurrencyStamp
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_Should_Rotate_ConcurrencyStamp()
    {
        var conv = CreateConversation();
        var stamp0 = conv.ConcurrencyStamp;

        conv.Rename("New Title");

        conv.Title.ShouldBe("New Title");
        conv.ConcurrencyStamp.ShouldNotBe(stamp0);
    }

    [Fact]
    public void UpdateAgentSession_Should_Rotate_ConcurrencyStamp()
    {
        var conv = CreateConversation();
        var stampBefore = conv.ConcurrencyStamp;

        conv.UpdateAgentSession("{\"key\":\"value\"}");

        conv.AgentSessionJson.ShouldBe("{\"key\":\"value\"}");
        conv.ConcurrencyStamp.ShouldNotBe(stampBefore);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 8. 每个 mutate 方法都会生成不同的 ConcurrencyStamp（无碰撞）
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Each_Mutate_Produces_Different_ConcurrencyStamp()
    {
        var conv = CreateConversation();
        var clock = CreateClock();

        var s0 = conv.ConcurrencyStamp;
        conv.Rename("A");
        var s1 = conv.ConcurrencyStamp;
        conv.AppendUserMessage(clock, Guid.NewGuid(), "Hello", Guid.NewGuid());
        var s2 = conv.ConcurrencyStamp;
        conv.AppendAssistantMessage(clock, Guid.NewGuid(), "World", null);
        var s3 = conv.ConcurrencyStamp;
        conv.UpdateAgentSession("{}");
        var s4 = conv.ConcurrencyStamp;

        new[] { s0, s1, s2, s3, s4 }.ShouldBeUnique();
    }
}
