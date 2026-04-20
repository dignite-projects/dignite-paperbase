namespace Dignite.Paperbase.AI.Evaluation;

public class EvaluationThresholds
{
    public double MinAccuracy { get; set; } = 0.85;
    public double MinAvgConfidenceOnCorrect { get; set; } = 0.70;
    public long MaxP95LatencyMs { get; set; } = 5000;
    public double MaxAvgCostUsd { get; set; } = 0.01;
}
