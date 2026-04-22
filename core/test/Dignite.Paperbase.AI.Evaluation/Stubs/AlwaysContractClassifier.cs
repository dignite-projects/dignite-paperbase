using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Evaluation.Stubs;

/// <summary>
/// Always returns "contract.general" with 0.9 confidence -- baseline stub for evaluation harness smoke test.
/// </summary>
public class AlwaysContractClassifier : IDocumentClassifier
{
    public const string TypeCode = "contract.general";
    public const double Confidence = 0.9;

    public Task<ClassificationResult> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ClassificationResult
        {
            TypeCode = TypeCode,
            ConfidenceScore = Confidence,
            Candidates = new List<TypeCandidate>
            {
                new TypeCandidate { TypeCode = TypeCode, ConfidenceScore = Confidence }
            }
        });
    }
}
