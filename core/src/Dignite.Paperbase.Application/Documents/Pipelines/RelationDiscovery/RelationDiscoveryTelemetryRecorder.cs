using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.RelationDiscovery;

/// <summary>
/// Issue #123: structured telemetry for L2/L3 RelationDiscovery — counters / histograms exported via
/// <see cref="System.Diagnostics.Metrics"/> and a paired structured log line per metric event.
///
/// <para>
/// <strong>Why a project-specific recorder vs ABP audit log</strong>: BackgroundJob runs are not in
/// HTTP context and have no audit scope by default; we need metrics, not audit rows. The
/// log lines are kept (not replaced) to preserve developer-grade observability.
/// </para>
///
/// <para>
/// <strong>Tag policy</strong>: tags are low-cardinality enums or buckets. <c>tenant_id</c> is
/// intentionally NOT a tag (would cause cardinality explosion in multi-tenant deployments
/// and isn't needed for ops dashboards — per-tenant drill-down via traces / logs instead).
/// </para>
/// </summary>
public class RelationDiscoveryTelemetryRecorder : ISingletonDependency
{
    public const string MeterName = "Dignite.Paperbase.Documents.RelationDiscovery";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> RunsTotal = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.runs.total",
        description: "RelationDiscovery background job executions, by result (succeeded / failed / document_missing).");

    private static readonly Histogram<long> L2Created = Meter.CreateHistogram<long>(
        "paperbase.relation_discovery.l2.created",
        description: "AiSuggested DocumentRelations created by L2 (structured fan-out) per run.");

    /// <summary>
    /// 硬伤三 visibility: how many identifiers each provider contributed for a given source
    /// document. Tag <c>provider</c> is the provider type name. Use this to spot a provider
    /// that suddenly stops producing identifiers (LLM extraction regressed, schema migration
    /// dropped fields, etc.). Recorded once per (run, provider) pair regardless of count;
    /// the histogram value is the count.
    /// </summary>
    private static readonly Histogram<long> L2IdentifiersByProvider = Meter.CreateHistogram<long>(
        "paperbase.relation_discovery.l2.identifiers_by_provider",
        description: "Number of identifiers emitted per provider per source document. Tags: provider.");

    /// <summary>
    /// 硬伤三 visibility: documents that produced zero identifiers across all providers.
    /// A spike means either no business module owns these documents OR business-module
    /// extraction is failing silently (LLM down, wrong fields, etc.). Either way it's the
    /// "L2 looks dead" signal operators need.
    /// </summary>
    private static readonly Counter<long> L2OrphanDocuments = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.l2.orphan_documents",
        description: "Documents that produced 0 identifiers (no module owns them, or extraction failed).");

    /// <summary>
    /// 硬伤三 visibility: high-ambiguity identifiers — an identifier value that matched
    /// MORE than <see cref="HighAmbiguityPeerThreshold"/> peer documents in a single L2
    /// run. Tag <c>type</c> identifies which DocumentIdentifierTypes value is ambiguous;
    /// repeated hits over time identify identifier categories that should be excluded
    /// (the way <c>ContractIdentifierProvider</c> already excludes PartyName).
    /// </summary>
    private static readonly Counter<long> L2HighAmbiguityIdentifiers = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.l2.high_ambiguity_identifiers",
        description: "Identifier values that matched too many peers (>= threshold). Tags: type.");

    /// <summary>Threshold for an identifier to be flagged as high-ambiguity. Aggressive on
    /// purpose — once an identifier matches this many distinct peers in one run it's almost
    /// certainly noise (e.g. LLM hallucinated a common word as a contract number).</summary>
    public const int HighAmbiguityPeerThreshold = 10;

    private static readonly Counter<long> L3Invoked = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.l3.invoked",
        description: "Times L3 (semantic + LLM fallback) was invoked because L2 found zero peers.");

    private static readonly Counter<long> L3LlmCalls = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.l3.llm_calls",
        description: "Per-candidate LLM evaluations by L3, by result (confirmed / rejected / error).");

    private static readonly Histogram<long> L3Created = Meter.CreateHistogram<long>(
        "paperbase.relation_discovery.l3.created",
        description: "AiSuggested DocumentRelations created by L3 per run (only when L3 invoked).");

    /// <summary>
    /// Y4 funnel granularity: how many candidates vector recall returned (pre-dedup). Combined with
    /// <see cref="L3CandidatesEvaluated"/> this distinguishes "0 created because recall was empty"
    /// from "0 created because everything was already linked" from "0 created because LLM rejected all".
    /// </summary>
    private static readonly Histogram<long> L3CandidatesRecalled = Meter.CreateHistogram<long>(
        "paperbase.relation_discovery.l3.candidates_recalled",
        description: "Vector-recall candidates before alreadyLinked / dismissal dedup.");

    /// <summary>
    /// Y4 funnel granularity: how many candidates actually reached LLM evaluation (post-dedup,
    /// pre-circuit-break). recalled − evaluated = filtered as already-linked-or-dismissed.
    /// evaluated − (confirmed + rejected + error) = skipped by circuit break.
    /// </summary>
    private static readonly Histogram<long> L3CandidatesEvaluated = Meter.CreateHistogram<long>(
        "paperbase.relation_discovery.l3.candidates_evaluated",
        description: "Candidates that reached the LLM evaluation step (post-dedup, pre-circuit-break).");

    /// <summary>
    /// Y4 funnel granularity: count of runs where the consecutive-failure circuit broke. Pairs
    /// with <see cref="L3CandidatesEvaluated"/> &lt; (alreadyLinked-filtered candidates) to diagnose
    /// "L3 looks dead" — provider down vs operator disabled vs everything-already-linked.
    /// </summary>
    private static readonly Counter<long> L3CircuitBroken = Meter.CreateCounter<long>(
        "paperbase.relation_discovery.l3.circuit_broken",
        description: "Runs where consecutive-failure circuit broke during L3 candidate evaluation.");

    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "paperbase.relation_discovery.duration",
        unit: "ms",
        description: "Wall-clock duration per RelationDiscovery layer (l2 / l3 / total).");

    /// <summary>
    /// AiSuggested → Manual conversions. The ONLY ground-truth signal for L2/L3 quality —
    /// the model's self-reported <see cref="DocumentRelation.Confidence"/> is not ground truth.
    /// </summary>
    private static readonly Counter<long> SuggestionConfirmed = Meter.CreateCounter<long>(
        "paperbase.relation.suggestion.confirmed",
        description: "User accepted an AiSuggested DocumentRelation (Confirm). Tags: source, confidence_bucket.");

    /// <summary>
    /// AiSuggested deletions. Paired with <see cref="SuggestionConfirmed"/> for the accept-rate funnel:
    /// <c>accept_rate = confirmed / (confirmed + rejected)</c>.
    /// </summary>
    private static readonly Counter<long> SuggestionRejected = Meter.CreateCounter<long>(
        "paperbase.relation.suggestion.rejected",
        description: "User deleted an AiSuggested DocumentRelation (Delete). Tags: source, confidence_bucket.");

    private readonly ILogger<RelationDiscoveryTelemetryRecorder> _logger;

    public RelationDiscoveryTelemetryRecorder(ILogger<RelationDiscoveryTelemetryRecorder> logger)
    {
        _logger = logger;
    }

    public virtual void RecordRun(RelationDiscoveryRunMetrics metrics)
    {
        RunsTotal.Add(1, new KeyValuePair<string, object?>("result", metrics.Result.ToString()));

        if (metrics.L2CreatedCount.HasValue)
        {
            L2Created.Record(metrics.L2CreatedCount.Value);
        }

        if (metrics.L3Invoked)
        {
            L3Invoked.Add(1);
            if (metrics.L3CreatedCount.HasValue)
            {
                L3Created.Record(metrics.L3CreatedCount.Value);
            }
            if (metrics.L3CandidatesRecalled.HasValue)
            {
                L3CandidatesRecalled.Record(metrics.L3CandidatesRecalled.Value);
            }
            if (metrics.L3CandidatesEvaluated.HasValue)
            {
                L3CandidatesEvaluated.Record(metrics.L3CandidatesEvaluated.Value);
            }
            if (metrics.L3CircuitBroken)
            {
                L3CircuitBroken.Add(1);
            }
        }

        if (metrics.L2DurationMs.HasValue)
        {
            RunDuration.Record(metrics.L2DurationMs.Value, new KeyValuePair<string, object?>("layer", "l2"));
        }
        if (metrics.L3DurationMs.HasValue)
        {
            RunDuration.Record(metrics.L3DurationMs.Value, new KeyValuePair<string, object?>("layer", "l3"));
        }
        if (metrics.TotalDurationMs.HasValue)
        {
            RunDuration.Record(metrics.TotalDurationMs.Value, new KeyValuePair<string, object?>("layer", "total"));
        }

        if (metrics.Result == RelationDiscoveryRunResult.Succeeded)
        {
            _logger.LogInformation(
                "RelationDiscovery run succeeded. DocumentId={DocumentId} L2Created={L2Created} L3Invoked={L3Invoked} L3Created={L3Created} L2Ms={L2Ms} L3Ms={L3Ms} TotalMs={TotalMs}",
                metrics.DocumentId,
                metrics.L2CreatedCount,
                metrics.L3Invoked,
                metrics.L3CreatedCount,
                metrics.L2DurationMs,
                metrics.L3DurationMs,
                metrics.TotalDurationMs);
        }
        else
        {
            _logger.LogWarning(
                "RelationDiscovery run did not complete normally. DocumentId={DocumentId} Result={Result} FailureReason={FailureReason} TotalMs={TotalMs}",
                metrics.DocumentId,
                metrics.Result,
                metrics.FailureReason,
                metrics.TotalDurationMs);
        }
    }

    public virtual void RecordL3LlmCall(RelationDiscoveryL3CallResult result)
    {
        L3LlmCalls.Add(1, new KeyValuePair<string, object?>("result", result.ToString()));
    }

    public virtual void RecordSuggestionConfirmed(RelationSource originalSource, double? confidence)
    {
        SuggestionConfirmed.Add(1,
            new KeyValuePair<string, object?>("source", originalSource.ToString()),
            new KeyValuePair<string, object?>("confidence_bucket", BucketConfidence(confidence)));
    }

    public virtual void RecordSuggestionRejected(RelationSource originalSource, double? confidence)
    {
        SuggestionRejected.Add(1,
            new KeyValuePair<string, object?>("source", originalSource.ToString()),
            new KeyValuePair<string, object?>("confidence_bucket", BucketConfidence(confidence)));
    }

    /// <summary>
    /// 硬伤三: record per-provider identifier contribution. Called once per (run, provider)
    /// pair. <paramref name="count"/> may be 0 — recording 0s lets dashboards see "this
    /// provider exists but didn't fire for this document" vs "this provider isn't installed."
    /// </summary>
    public virtual void RecordIdentifiersByProvider(string providerName, int count)
    {
        L2IdentifiersByProvider.Record(count,
            new KeyValuePair<string, object?>("provider", providerName));
    }

    /// <summary>
    /// 硬伤三: a source document arrived at L2 with zero identifiers across all providers.
    /// </summary>
    public virtual void RecordOrphanDocument()
    {
        L2OrphanDocuments.Add(1);
    }

    /// <summary>
    /// 硬伤三: a single (type, value) identifier matched too many peer documents in one
    /// run — almost certainly noise (LLM hallucinated a generic phrase as an identifier,
    /// or the type is over-broad). <paramref name="peerCount"/> recorded as a counter
    /// increment of 1 with the type as tag; the count itself goes to the structured log
    /// so operators can see the egregious values.
    /// </summary>
    public virtual void RecordHighAmbiguityIdentifier(string identifierType, string normalizedValue, int peerCount)
    {
        L2HighAmbiguityIdentifiers.Add(1,
            new KeyValuePair<string, object?>("type", identifierType));
        _logger.LogWarning(
            "L2 RelationDiscovery: high-ambiguity identifier matched {PeerCount} peers (threshold {Threshold}). " +
            "Type={IdentifierType} NormalizedValue={NormalizedValue}. Treat as noise candidate; " +
            "consider excluding this type from the provider's SupportedIdentifierTypes.",
            peerCount, HighAmbiguityPeerThreshold, identifierType, normalizedValue);
    }

    /// <summary>
    /// Bucket continuous confidence into 4 fixed buckets aligned with
    /// <see cref="Ai.PaperbaseAIBehaviorOptions.SemanticRelationDiscoveryConfidenceThreshold"/> = 0.7
    /// (anything below threshold should never reach storage, so &lt;0.7 is a "shouldn't happen" signal).
    /// </summary>
    protected virtual string BucketConfidence(double? confidence)
    {
        if (!confidence.HasValue) return "(none)";   // Manual relations have null confidence
        if (confidence.Value < 0.7) return "<0.7";
        if (confidence.Value < 0.8) return "0.7-0.8";
        if (confidence.Value < 0.9) return "0.8-0.9";
        return "0.9+";
    }
}

/// <summary>Per-run metrics emitted by <see cref="RelationDiscoveryBackgroundJob"/> at completion.</summary>
public sealed record RelationDiscoveryRunMetrics
{
    public required Guid DocumentId { get; init; }
    public required RelationDiscoveryRunResult Result { get; init; }

    /// <summary>Number of AiSuggested relations L2 created (null = L2 didn't run, e.g. document missing).</summary>
    public int? L2CreatedCount { get; init; }

    /// <summary>True when L2 returned 0 and L3 was invoked.</summary>
    public bool L3Invoked { get; init; }

    /// <summary>Number of AiSuggested relations L3 created (null = L3 didn't run).</summary>
    public int? L3CreatedCount { get; init; }

    /// <summary>Y4: how many candidates vector recall returned (pre-dedup). null = L3 didn't run.</summary>
    public int? L3CandidatesRecalled { get; init; }

    /// <summary>Y4: how many candidates reached the LLM evaluation step (post-alreadyLinked dedup,
    /// pre-circuit-break). null = L3 didn't run.</summary>
    public int? L3CandidatesEvaluated { get; init; }

    /// <summary>Y4: true when the consecutive-failure circuit broke during L3 candidate evaluation.</summary>
    public bool L3CircuitBroken { get; init; }

    public double? L2DurationMs { get; init; }
    public double? L3DurationMs { get; init; }
    public double? TotalDurationMs { get; init; }

    /// <summary>Set when <see cref="Result"/> is <see cref="RelationDiscoveryRunResult.Failed"/>.</summary>
    public string? FailureReason { get; init; }
}

public enum RelationDiscoveryRunResult
{
    Succeeded = 0,
    Failed = 1,
    DocumentMissing = 2
}

/// <summary>Per-candidate LLM evaluation result inside L3.</summary>
public enum RelationDiscoveryL3CallResult
{
    /// <summary>LLM returned <c>IsRelated=true</c> with confidence ≥ threshold; relation created.</summary>
    Confirmed = 0,

    /// <summary>LLM returned <c>IsRelated=false</c> OR confidence below threshold; no relation created.</summary>
    Rejected = 1,

    /// <summary>LLM call threw — candidate dropped, others continue (per-candidate isolation).</summary>
    Error = 2
}
