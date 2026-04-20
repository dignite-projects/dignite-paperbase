using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Evaluation;

public class ClassificationEvaluationRunner
{
    private readonly IDocumentClassifier _classifier;

    public ClassificationEvaluationRunner(IDocumentClassifier classifier)
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
                var request = new ClassificationRequest
                {
                    ExtractedText = fixture.SampleText ?? string.Empty
                };

                var result = await _classifier.ClassifyAsync(request, cancellationToken);
                actualTypeCode = result.TypeCode ?? string.Empty;
                confidence = result.ConfidenceScore;
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
