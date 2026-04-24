using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.AI.Evaluation.Stubs;

/// <summary>
/// Always returns "contract.general" with 0.9 confidence -- baseline stub for evaluation harness smoke test.
/// </summary>
public static class AlwaysContractClassifier
{
    public const string TypeCode = "contract.general";
    public const double Confidence = 0.9;

    public static Task<ClassificationDelegateResult> ClassifyAsync(
        string extractedText, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ClassificationDelegateResult
        {
            TypeCode = TypeCode,
            Confidence = Confidence
        });
    }
}
