using System;
using System.IO;
using System.Threading.Tasks;
using Dignite.Paperbase.AI.Evaluation.Stubs;
using Xunit;
using Xunit.Abstractions;

namespace Dignite.Paperbase.AI.Evaluation;

/// <summary>
/// Smoke test: runs the evaluation harness with the AlwaysInvoiceClassifier stub.
/// Verifies the harness wiring is correct and produces a baseline accuracy report.
/// The stub returns invoice.qualified for everything, so accuracy = (positive fixtures) / total.
/// </summary>
public class StubClassifierEvaluationTests
{
    private readonly ITestOutputHelper _output;
    private static readonly string FixturesDir = Path.Combine(
        AppContext.BaseDirectory, "fixtures");

    public StubClassifierEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task StubClassifier_Harness_Wires_Up_And_Produces_Baseline()
    {
        var fixtures = FixtureLoader.LoadClassificationFixtures(FixturesDir);
        Assert.True(fixtures.Count >= 20, $"Expected at least 20 fixtures, found {fixtures.Count}");

        var runner = new ClassificationEvaluationRunner(new AlwaysInvoiceClassifier());
        var report = await runner.RunAsync(fixtures);

        _output.WriteLine($"Total:                   {report.Total}");
        _output.WriteLine($"Correct:                 {report.Correct}");
        _output.WriteLine($"Accuracy:                {report.Accuracy:P1}");
        _output.WriteLine($"AvgConfidenceOnCorrect:  {report.AvgConfidenceOnCorrect:F3}");
        _output.WriteLine($"P95 Latency:             {report.P95LatencyMs} ms");

        // The harness must process all fixtures without crashing
        Assert.Equal(fixtures.Count, report.Total);
        Assert.True(report.Correct >= 0);

        // Stub returns only invoice.qualified — positive fixtures should all be correct
        foreach (var c in report.Cases)
        {
            if (c.ExpectedTypeCode == AlwaysInvoiceClassifier.TypeCode)
            {
                Assert.True(c.IsCorrect, $"Fixture {c.FixtureId} expected invoice.qualified but got {c.ActualTypeCode}");
                Assert.Equal(AlwaysInvoiceClassifier.Confidence, c.Confidence);
            }
        }
    }

    [Fact]
    public async Task Thresholds_File_Is_Present_And_Parseable()
    {
        var thresholds = FixtureLoader.LoadThresholds(FixturesDir);
        Assert.True(thresholds.MinAccuracy > 0 && thresholds.MinAccuracy <= 1.0);
        Assert.True(thresholds.MaxP95LatencyMs > 0);
        await Task.CompletedTask;
    }
}
