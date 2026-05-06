using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dignite.Paperbase.Abstractions.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Chat.Telemetry;

// Singleton lifetime matches the static Meter / instruments below — `System.Diagnostics.Metrics`
// recommends one shared Meter per process, and per-scope construction would only churn allocations.
public class DocumentChatTelemetryRecorder : ISingletonDependency
{
    public const string AuditToolCallsPropertyName = "DocumentChat.ToolCalls";
    public const string AuditTurnPropertyName = "DocumentChat.Turn";

    private const int MaxAuditCommentLength = 2048;
    private static readonly Meter Meter = new("Dignite.Paperbase.DocumentChat");

    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "paperbase.document_chat.tool.calls");
    private static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        "paperbase.document_chat.tool.duration", unit: "ms");
    private static readonly Histogram<long> ToolResultSize = Meter.CreateHistogram<long>(
        "paperbase.document_chat.tool.result.size", unit: "By");
    private static readonly Counter<long> Turns = Meter.CreateCounter<long>(
        "paperbase.document_chat.turns");
    private static readonly Counter<long> DegradedTurns = Meter.CreateCounter<long>(
        "paperbase.document_chat.turn.degraded");
    private static readonly Histogram<double> TurnDuration = Meter.CreateHistogram<double>(
        "paperbase.document_chat.turn.duration", unit: "ms");
    private static readonly Counter<long> InputTokens = Meter.CreateCounter<long>(
        "paperbase.document_chat.tokens.input");
    private static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>(
        "paperbase.document_chat.tokens.output");
    private static readonly Counter<long> TotalTokens = Meter.CreateCounter<long>(
        "paperbase.document_chat.tokens.total");

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

        var tags = CreateToolTags(entry);
        ToolCalls.Add(1, tags);
        ToolDuration.Record(entry.ElapsedMs, tags);
        if (entry.ResultSizeBytes.HasValue)
        {
            ToolResultSize.Record(entry.ResultSizeBytes.Value, tags);
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

        var tags = CreateTurnTags(entry);
        Turns.Add(1, tags);
        TurnDuration.Record(entry.ElapsedMs, tags);
        // IsDegraded is the project's "honest signal" (CLAUDE.md) — count it as a
        // first-class metric so cost-governance dashboards can alarm on classes of
        // questions answered without retrieval.
        if (entry.IsDegraded)
        {
            DegradedTurns.Add(1, tags);
        }
        if (entry.InputTokenCount.HasValue)
        {
            InputTokens.Add(entry.InputTokenCount.Value, tags);
        }
        if (entry.OutputTokenCount.HasValue)
        {
            OutputTokens.Add(entry.OutputTokenCount.Value, tags);
        }
        if (entry.TotalTokenCount.HasValue)
        {
            TotalTokens.Add(entry.TotalTokenCount.Value, tags);
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

    public virtual string HashMessage(string message)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public virtual DocumentChatTokenUsageSummary SummarizeUsage(UsageDetails? usage)
        => usage == null
            ? new DocumentChatTokenUsageSummary { UsageAvailable = false }
            : new DocumentChatTokenUsageSummary
            {
                UsageAvailable = true,
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                TotalTokenCount = usage.TotalTokenCount,
                CachedInputTokenCount = usage.CachedInputTokenCount,
                ReasoningTokenCount = usage.ReasoningTokenCount
            };

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
        AddAuditComment(scope, "document-chat-tool", entry);
    }

    private void AddTurnToAuditLog(DocumentChatTurnAuditEntry entry)
    {
        var scope = _auditingManager.Current;
        if (scope?.Log == null)
        {
            return;
        }

        scope.Log.ExtraProperties[AuditTurnPropertyName] = entry;
        AddAuditComment(scope, "document-chat-turn", entry);
    }

    private static void AddAuditComment(IAuditLogScope scope, string eventName, object entry)
    {
        var json = JsonSerializer.Serialize(entry);
        if (json.Length > MaxAuditCommentLength)
        {
            json = json[..MaxAuditCommentLength] + "...";
        }

        scope.Log.Comments.Add($"{eventName}: {json}");
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

public sealed class DocumentChatTurnAuditEntry
{
    public required Guid ConversationId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DocumentId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public string? TraceId { get; init; }
    public required bool Streaming { get; init; }
    public required string UserMessageHash { get; init; }
    public required int UserMessageLength { get; init; }
    public int CitationCount { get; init; }
    public bool IsDegraded { get; init; }
    public bool TokenUsageAvailable { get; init; }
    public long? InputTokenCount { get; init; }
    public long? OutputTokenCount { get; init; }
    public long? TotalTokenCount { get; init; }
    public long? CachedInputTokenCount { get; init; }
    public long? ReasoningTokenCount { get; init; }
    public required double ElapsedMs { get; init; }
    public required DocumentChatTelemetryOutcome Outcome { get; init; }
    public string? ExceptionType { get; init; }
}

public sealed class DocumentChatTokenUsageSummary
{
    public required bool UsageAvailable { get; init; }
    public long? InputTokenCount { get; init; }
    public long? OutputTokenCount { get; init; }
    public long? TotalTokenCount { get; init; }
    public long? CachedInputTokenCount { get; init; }
    public long? ReasoningTokenCount { get; init; }
}
