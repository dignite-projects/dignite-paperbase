using System;
using System.Linq;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Documents.AI.Workflows;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.Classification;

/// <summary>
/// 关键词匹配分类器：在 AI Provider 失败时作为兜底。
/// </summary>
public class KeywordDocumentClassifier : ITransientDependency
{
    private readonly DocumentTypeOptions _options;

    public KeywordDocumentClassifier(IOptions<DocumentTypeOptions> options)
    {
        _options = options.Value;
    }

    public virtual DocumentClassificationOutcome Classify(string extractedText)
    {
        var text = extractedText ?? string.Empty;

        var match = _options.Types
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault(t => t.MatchKeywords.Any(kw =>
                text.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            return new DocumentClassificationOutcome
            {
                TypeCode = match.TypeCode,
                ConfidenceScore = 0.9
            };
        }

        return new DocumentClassificationOutcome
        {
            TypeCode = null,
            ConfidenceScore = 0.0
        };
    }
}
