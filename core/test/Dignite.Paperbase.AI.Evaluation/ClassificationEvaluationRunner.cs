using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.AI.Evaluation;

/// <summary>
/// Classifier delegate signature: takes the extracted text, returns the predicted TypeCode + confidence.
/// </summary>
public delegate Task<ClassificationDelegateResult> ClassifierDelegate(
    string extractedText, CancellationToken cancellationToken);

public class ClassificationDelegateResult
{
    public string? TypeCode { get; set; }
    public double Confidence { get; set; }
}

public class ClassificationEvaluationRunner
{
    private readonly ClassifierDelegate _classifier;

    public ClassificationEvaluationRunner(ClassifierDelegate classifier)
    {
        _classifier = classifier;
    }

    public async Task<EvaluationReport> RunAsync(
        IReadOnlyList<ClassificationFixture> fixtures,
        CancellationToken cancellationToken = default)
    {
        var cases = new List<EvaluationCase>();

        foreach (var fixture in fixtures)
        {
            var sw = Stopwatch.StartNew();
            string actualTypeCode = string.Empty;
            double confidence = 0;
            string? errorMessage = null;

            try
            {
                var result = await _classifier(fixture.SampleText ?? string.Empty, cancellationToken);
                actualTypeCode = result.TypeCode ?? string.Empty;
                confidence = result.Confidence;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            sw.Stop();
            cases.Add(new EvaluationCase
            {
                FixtureId = fixture.Id,
                ExpectedTypeCode = fixture.Expected.TypeCode,
                ActualTypeCode = actualTypeCode,
                Confidence = confidence,
                LatencyMs = sw.ElapsedMilliseconds,
                CostUsd = 0,
                ErrorMessage = errorMessage
            });
        }

        return EvaluationReport.From(cases);
    }
}
