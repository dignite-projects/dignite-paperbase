using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Documents.AI.Workflows;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.Classification;

/// <summary>
/// 关键词匹配分类器：在 AI Provider 失败时作为兜底。
/// 候选集由调用方决定（与 LLM 路径保持一致），避免回退路径命中 LLM 看不见的类型。
/// </summary>
public class KeywordDocumentClassifier : ITransientDependency
{
    public virtual DocumentClassificationOutcome Classify(
        IReadOnlyList<DocumentTypeDefinition> candidates,
        string extractedText)
    {
        var text = extractedText ?? string.Empty;

        var match = candidates
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault(t => t.MatchKeywords.Any(kw =>
                text.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        if (match != null)
        {
            return new DocumentClassificationOutcome
            {
                TypeCode = match.TypeCode,
                ConfidenceScore = ClassificationDefaults.KeywordMatchConfidence
            };
        }

        return new DocumentClassificationOutcome
        {
            TypeCode = null,
            ConfidenceScore = 0.0
        };
    }
}
