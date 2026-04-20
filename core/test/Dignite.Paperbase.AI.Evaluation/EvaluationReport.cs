using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Paperbase.AI.Evaluation;

public class EvaluationReport
{
    public int Total { get; init; }
    public int Correct { get; init; }
    public double Accuracy => Total == 0 ? 0 : (double)Correct / Total;
    public double AvgConfidenceOnCorrect { get; init; }
    public long P95LatencyMs { get; init; }
    public double AvgCostUsd { get; init; }
    public IReadOnlyList<EvaluationCase> Cases { get; init; } = Array.Empty<EvaluationCase>();

    public bool PassesThresholds(EvaluationThresholds thresholds)
    {
        return Accuracy >= thresholds.MinAccuracy
            && AvgConfidenceOnCorrect >= thresholds.MinAvgConfidenceOnCorrect
            && P95LatencyMs <= thresholds.MaxP95LatencyMs
            && AvgCostUsd <= thresholds.MaxAvgCostUsd;
    }

    public static EvaluationReport From(IReadOnlyList<EvaluationCase> cases)
    {
        var correct = cases.Where(c => c.IsCorrect).ToList();
        var latencies = cases.Select(c => c.LatencyMs).OrderBy(l => l).ToList();
        long p95 = latencies.Count == 0 ? 0 : latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
        double avgConfidence = correct.Count == 0 ? 0 : correct.Average(c => c.Confidence);
        double avgCost = cases.Count == 0 ? 0 : cases.Average(c => c.CostUsd);

        return new EvaluationReport
        {
            Total = cases.Count,
            Correct = correct.Count,
            AvgConfidenceOnCorrect = avgConfidence,
            P95LatencyMs = p95,
            AvgCostUsd = avgCost,
            Cases = cases
        };
    }
}

public class EvaluationCase
{
    public string FixtureId { get; init; } = string.Empty;
    public string ExpectedTypeCode { get; init; } = string.Empty;
    public string ActualTypeCode { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public bool IsCorrect => ExpectedTypeCode == ActualTypeCode;
    public long LatencyMs { get; init; }
    public double CostUsd { get; init; }
    public string? ErrorMessage { get; init; }
}
