using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.Classification;

/// <summary>
/// 关键词匹配分类器（Slice 1 暴力版）。
/// Slice 3 替换为 AiDocumentClassifier，此类可直接删除。
/// </summary>
public class KeywordDocumentClassifier : IDocumentClassifier, ITransientDependency
{
    private readonly DocumentTypeOptions _options;

    public KeywordDocumentClassifier(IOptions<DocumentTypeOptions> options)
    {
        _options = options.Value;
    }

    public virtual Task<ClassificationResult> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = request.ExtractedText ?? string.Empty;

        var match = _options.Types
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault(t => t.MatchKeywords.Any(kw =>
                text.Contains(kw, System.StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            return Task.FromResult(new ClassificationResult
            {
                TypeCode = match.TypeCode,
                ConfidenceScore = 0.9
            });
        }

        return Task.FromResult(new ClassificationResult
        {
            TypeCode = null,
            ConfidenceScore = 0.0
        });
    }
}
