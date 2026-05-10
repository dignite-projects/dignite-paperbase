using System;
using System.Collections.Generic;
using Dignite.Paperbase.Chat.Telemetry;
using Shouldly;
using Volo.Abp.Auditing;
using Xunit;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Focused tests for <see cref="ChatTelemetryRecorder"/>'s per-turn enrichment
/// (Issue #98): <c>ToolCallSummary</c>, <c>ToolCallDepth</c>, <c>GroundingSource</c>
/// must be derived from the per-tool entries already on the audit scope.
/// <para>
/// These tests exercise the recorder directly inside an <see cref="IAuditingManager"/>
/// scope rather than going through <c>SendMessageAsync</c> — that lets us cover the
/// Structured / Mixed cases without standing up a fake business-module contributor.
/// </para>
/// </summary>
public class ChatTelemetryRecorder_Tests
    : PaperbaseApplicationTestBase<ChatAppServiceTestModule>
{
    private readonly ChatTelemetryRecorder _recorder;
    private readonly IAuditingManager _auditingManager;

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ChatTelemetryRecorder_Tests()
    {
        _recorder = GetRequiredService<ChatTelemetryRecorder>();
        _auditingManager = GetRequiredService<IAuditingManager>();
    }

    [Fact]
    public void RecordTurn_GroundingNone_WhenNoToolsCalled()
    {
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.GroundingSource.ShouldBe(GroundingSource.None);
        turn.ToolCallDepth.ShouldBe(0);
        turn.ToolCallSummary.ShouldBeNull();
    }

    [Fact]
    public void RecordTurn_GroundingVector_WhenOnlySearchToolCalled()
    {
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry(ChatConsts.SearchPaperbaseDocumentsToolName));

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.GroundingSource.ShouldBe(GroundingSource.Vector);
        turn.ToolCallDepth.ShouldBe(1);
        turn.ToolCallSummary.ShouldNotBeNull();
        turn.ToolCallSummary!.ShouldContainKeyAndValue(ChatConsts.SearchPaperbaseDocumentsToolName, 1);
    }

    [Fact]
    public void RecordTurn_GroundingStructured_WhenOnlyBusinessToolsCalled()
    {
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry("search_contracts"));
        _recorder.RecordToolCall(BuildToolEntry("get_contract_detail"));

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.GroundingSource.ShouldBe(GroundingSource.Structured);
        turn.ToolCallDepth.ShouldBe(2);
        turn.ToolCallSummary!.ShouldContainKeyAndValue("search_contracts", 1);
        turn.ToolCallSummary!.ShouldContainKeyAndValue("get_contract_detail", 1);
        turn.ToolCallSummary!.ShouldNotContainKey(ChatConsts.SearchPaperbaseDocumentsToolName);
    }

    [Fact]
    public void RecordTurn_GroundingMixed_WhenBothCalled()
    {
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry(ChatConsts.SearchPaperbaseDocumentsToolName));
        _recorder.RecordToolCall(BuildToolEntry("get_contract_aggregate"));

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.GroundingSource.ShouldBe(GroundingSource.Mixed);
        turn.ToolCallDepth.ShouldBe(2);
        turn.ToolCallSummary!.Count.ShouldBe(2);
    }

    [Fact]
    public void RecordTurn_AggregatesCountsAcrossMultipleInvocationsOfSameTool()
    {
        // Multi-step retrieval (motivating Issue #99): the model invokes
        // search_paperbase_documents twice in a single turn (once per
        // documentTypeCode). The telemetry must count both, not just the last.
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry(ChatConsts.SearchPaperbaseDocumentsToolName));
        _recorder.RecordToolCall(BuildToolEntry(ChatConsts.SearchPaperbaseDocumentsToolName));
        _recorder.RecordToolCall(BuildToolEntry("search_contracts"));

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.ToolCallDepth.ShouldBe(3);
        turn.ToolCallSummary!.ShouldContainKeyAndValue(ChatConsts.SearchPaperbaseDocumentsToolName, 2);
        turn.ToolCallSummary!.ShouldContainKeyAndValue("search_contracts", 1);
        turn.GroundingSource.ShouldBe(GroundingSource.Mixed);
    }

    [Fact]
    public void RecordTurn_PreservesCitationsTrimmedFromCaller()
    {
        // The capture knows whether MaxCapturedCitations clamped the result set; the
        // AppService passes that flag through to the recorder so the audit row carries
        // a single, faithful per-turn signal.
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry(ChatConsts.SearchPaperbaseDocumentsToolName));

        _recorder.RecordTurn(BuildTurnEntry(citationsTrimmed: true));

        var turn = ReadTurnFromAuditScope();
        turn.CitationsTrimmed.ShouldBeTrue();
    }

    [Fact]
    public void GetCurrentTurnGroundingSource_DelegatesToClassify()
    {
        // The AppService uses this helper to derive ChatTurnResultDto.GroundingSource
        // (and IsDegraded) BEFORE RecordTurn fires. It must produce the same
        // classification the audit entry will eventually receive — single source of truth.
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry("get_contract_aggregate"));

        _recorder.GetCurrentTurnGroundingSource().ShouldBe(GroundingSource.Structured);
    }

    [Fact]
    public void GetCurrentTurnGroundingSource_ReturnsNone_WhenNoScopeOrNoToolsCalled()
    {
        // No audit scope active (e.g. a code path that runs outside a scope by mistake)
        // must still return a sane value rather than throw — the AppService treats
        // None as "answer not grounded → IsDegraded".
        _recorder.GetCurrentTurnGroundingSource().ShouldBe(GroundingSource.None);

        using var auditScope = _auditingManager.BeginScope();
        _recorder.GetCurrentTurnGroundingSource().ShouldBe(GroundingSource.None);
    }

    [Fact]
    public void RecordTurn_CountsFailedInvocations_BecauseTheyReflectModelBehavior()
    {
        // A failed tool call still counts toward ToolCallDepth — telemetry is about
        // what the model attempted, not just what succeeded.
        using var auditScope = _auditingManager.BeginScope();

        _recorder.RecordToolCall(BuildToolEntry(
            ChatConsts.SearchPaperbaseDocumentsToolName,
            outcome: ChatTelemetryOutcome.Failure));

        _recorder.RecordTurn(BuildTurnEntry());

        var turn = ReadTurnFromAuditScope();
        turn.ToolCallDepth.ShouldBe(1);
        turn.GroundingSource.ShouldBe(GroundingSource.Vector);
    }

    private ChatTurnAuditEntry ReadTurnFromAuditScope()
    {
        var scope = _auditingManager.Current.ShouldNotBeNull();
        scope.Log.ExtraProperties.ShouldContainKey(ChatTelemetryRecorder.AuditTurnPropertyName);
        return scope.Log.ExtraProperties[ChatTelemetryRecorder.AuditTurnPropertyName]
            .ShouldBeOfType<ChatTurnAuditEntry>();
    }

    private static ChatTurnAuditEntry BuildTurnEntry(bool citationsTrimmed = false)
        => new()
        {
            ConversationId = ConversationId,
            Streaming = false,
            ElapsedMs = 1.0,
            Outcome = ChatTelemetryOutcome.Success,
            CitationsTrimmed = citationsTrimmed
        };

    private static ChatToolAuditEntry BuildToolEntry(
        string toolName,
        ChatTelemetryOutcome outcome = ChatTelemetryOutcome.Success)
        => new()
        {
            ConversationId = ConversationId,
            ToolName = toolName,
            ArgumentsSummary = new Dictionary<string, object?>(),
            ElapsedMs = 1.0,
            Outcome = outcome
        };
}
