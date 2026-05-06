using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Dignite.Paperbase.Abstractions.Chat;
using Microsoft.Extensions.Logging;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Chat.Telemetry;

/// <summary>
/// Project-specific telemetry layer on top of the standard signals already emitted by
/// <c>Microsoft.Extensions.AI</c>'s <c>OpenTelemetryChatClient</c> /
/// <c>FunctionInvokingChatClient</c> when the host wires <c>.UseOpenTelemetry()</c>.
/// <para>
/// Standard signals (do NOT duplicate here):
/// <list type="bullet">
///   <item><c>gen_ai.client.operation.duration</c> — turn latency (s) histogram</item>
///   <item><c>gen_ai.client.token.usage</c> — input/output tokens histogram</item>
///   <item><c>gen_ai.client.operation.time_to_first_chunk</c> — streaming TTFB histogram</item>
///   <item>Activity span <c>"chat {model}"</c> — turn span with <c>gen_ai.*</c> tags</item>
///   <item>Activity span <c>"execute_tool {tool_name}"</c> — per-tool span</item>
/// </list>
/// </para>
/// <para>
/// What this recorder adds beyond the OTel standard:
/// <list type="bullet">
///   <item><c>paperbase.document_chat.turn.degraded</c> — counter for the project's
///     "honest signal" (CLAUDE.md): turns where the model declined to invoke search
///     OR retrieval threw and the turn fell back to context-only.</item>
///   <item><c>paperbase.document_chat.tool.result.size</c> — histogram of result
///     payload size (bytes), useful for spotting pathological tool outputs that
///     blow up LLM context.</item>
///   <item>Business-domain audit entries on <see cref="IAuditingManager"/>:
///     tenant/user/conversation/document/document-type — these are not in OTel scope
///     and link to the OTel trace via <c>Activity.Current?.TraceId</c>.</item>
/// </list>
/// </para>
/// </summary>
// Singleton lifetime matches the static Meter / instruments below — `System.Diagnostics.Metrics`
// recommends one shared Meter per process, and per-scope construction would only churn allocations.
public class DocumentChatTelemetryRecorder : ISingletonDependency
{
    public const string AuditToolCallsPropertyName = "DocumentChat.ToolCalls";
    public const string AuditTurnPropertyName = "DocumentChat.Turn";
    public const string MeterName = "Dignite.Paperbase.DocumentChat";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DegradedTurns = Meter.CreateCounter<long>(
        "paperbase.document_chat.turn.degraded",
        description: "Number of chat turns that ran without retrieval grounding (model declined to invoke search OR retrieval failed).");
    private static readonly Histogram<long> ToolResultSize = Meter.CreateHistogram<long>(
        "paperbase.document_chat.tool.result.size", unit: "By",
        description: "Size of tool-call result payloads in bytes.");

    private readonly IAuditingManager _auditingManager;
    private readonly ILogger<DocumentChatTelemetryRecorder> _logger;

    public DocumentChatTelemetryRecorder(
        IAuditingManager auditingManager,
        ILogger<DocumentChatTelemetryRecorder> logger)
    {
        _auditingManager = auditingManager;
        _logger = logger;
    }

    public virtual void RecordToolCall(DocumentChatToolAuditEntry entry)
    {
        AddToolCallToAuditLog(entry);

        // Per-tool count + duration are emitted by Microsoft.Extensions.AI's
        // FunctionInvocationProcessor as `execute_tool {tool_name}` Activity spans
        // (see OTel GenAI semantic conventions). Don't duplicate here.
        if (entry.ResultSizeBytes.HasValue)
        {
            ToolResultSize.Record(entry.ResultSizeBytes.Value, CreateToolTags(entry));
        }

        if (entry.Outcome == DocumentChatTelemetryOutcome.Success)
        {
            _logger.LogInformation(
                "Document chat tool {ToolName} completed. ConversationId={ConversationId} TenantId={TenantId} DocumentTypeCode={DocumentTypeCode} ElapsedMs={ElapsedMs} ResultSizeBytes={ResultSizeBytes}",
                entry.ToolName,
                entry.ConversationId,
                entry.TenantId,
                entry.DocumentTypeCode,
                entry.ElapsedMs,
                entry.ResultSizeBytes);
        }
        else
        {
            _logger.LogWarning(
                "Document chat tool {ToolName} failed. ConversationId={ConversationId} TenantId={TenantId} DocumentTypeCode={DocumentTypeCode} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType}",
                entry.ToolName,
                entry.ConversationId,
                entry.TenantId,
                entry.DocumentTypeCode,
                entry.ElapsedMs,
                entry.ExceptionType);
        }
    }

    public virtual void RecordTurn(DocumentChatTurnAuditEntry entry)
    {
        AddTurnToAuditLog(entry);

        // Turn count + duration + token usage are emitted by
        // Microsoft.Extensions.AI's OpenTelemetryChatClient as standard
        // gen_ai.* signals — don't duplicate here.
        // IsDegraded is the project's "honest signal" (CLAUDE.md) and has no
        // OTel-standard equivalent, so we count it explicitly.
        if (entry.IsDegraded)
        {
            DegradedTurns.Add(1, CreateTurnTags(entry));
        }

        if (entry.Outcome == DocumentChatTelemetryOutcome.Success)
        {
            _logger.LogInformation(
                "Document chat turn completed. ConversationId={ConversationId} TenantId={TenantId} DocumentTypeCode={DocumentTypeCode} Streaming={Streaming} IsDegraded={IsDegraded} CitationCount={CitationCount} ElapsedMs={ElapsedMs}",
                entry.ConversationId,
                entry.TenantId,
                entry.DocumentTypeCode,
                entry.Streaming,
                entry.IsDegraded,
                entry.CitationCount,
                entry.ElapsedMs);
        }
        else
        {
            _logger.LogWarning(
                "Document chat turn failed. ConversationId={ConversationId} TenantId={TenantId} DocumentTypeCode={DocumentTypeCode} Streaming={Streaming} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType}",
                entry.ConversationId,
                entry.TenantId,
                entry.DocumentTypeCode,
                entry.Streaming,
                entry.ElapsedMs,
                entry.ExceptionType);
        }
    }

    private void AddToolCallToAuditLog(DocumentChatToolAuditEntry entry)
    {
        var scope = _auditingManager.Current;
        if (scope?.Log == null)
        {
            return;
        }

        if (!scope.Log.ExtraProperties.TryGetValue(AuditToolCallsPropertyName, out var existing)
            || existing is not List<DocumentChatToolAuditEntry> entries)
        {
            entries = new List<DocumentChatToolAuditEntry>();
            scope.Log.ExtraProperties[AuditToolCallsPropertyName] = entries;
        }

        entries.Add(entry);
    }

    private void AddTurnToAuditLog(DocumentChatTurnAuditEntry entry)
    {
        var scope = _auditingManager.Current;
        if (scope?.Log == null)
        {
            return;
        }

        scope.Log.ExtraProperties[AuditTurnPropertyName] = entry;
    }

    private static KeyValuePair<string, object?>[] CreateToolTags(DocumentChatToolAuditEntry entry)
        => new[]
        {
            new KeyValuePair<string, object?>("tool.name", entry.ToolName),
            new KeyValuePair<string, object?>("outcome", entry.Outcome.ToString()),
            new KeyValuePair<string, object?>("document.type", entry.DocumentTypeCode ?? "(none)")
        };

    private static KeyValuePair<string, object?>[] CreateTurnTags(DocumentChatTurnAuditEntry entry)
        => new[]
        {
            new KeyValuePair<string, object?>("outcome", entry.Outcome.ToString()),
            new KeyValuePair<string, object?>("document.type", entry.DocumentTypeCode ?? "(none)"),
            new KeyValuePair<string, object?>("streaming", entry.Streaming)
        };
}

public enum DocumentChatTelemetryOutcome
{
    Success = 0,
    Failure = 1
}

public sealed class DocumentChatToolAuditEntry
{
    public required Guid ConversationId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public string? TraceId { get; init; }
    public required string ToolName { get; init; }
    public required IReadOnlyDictionary<string, object?> ArgumentsSummary { get; init; }
    public IReadOnlyDictionary<string, object?>? ResultSummary { get; init; }
    public long? ResultSizeBytes { get; init; }
    public required double ElapsedMs { get; init; }
    public required DocumentChatTelemetryOutcome Outcome { get; init; }
    public string? ExceptionType { get; init; }
}

// Token usage (input/output/cached/reasoning) is a Microsoft.Extensions.AI signal:
// when the host wires `OpenTelemetryChatClient` (see PaperbaseHostModule.ConfigureAI),
// the gen_ai.client.token.usage histogram emits each turn's token counts. Audit
// rows correlate with that telemetry through TraceId; carrying the raw counts on
// the audit entry as well would only re-emit the same data.
public sealed class DocumentChatTurnAuditEntry
{
    public required Guid ConversationId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public string? TraceId { get; init; }
    public required bool Streaming { get; init; }
    public int CitationCount { get; init; }
    public bool IsDegraded { get; init; }
    public required double ElapsedMs { get; init; }
    public required DocumentChatTelemetryOutcome Outcome { get; init; }
    public string? ExceptionType { get; init; }
}
