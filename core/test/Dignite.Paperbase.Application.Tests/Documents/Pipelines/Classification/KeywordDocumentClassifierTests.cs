using System.Collections.Generic;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Documents.Pipelines.Classification;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// KeywordDocumentClassifier 行为测试。
/// 验证候选集由调用方决定（与 LLM 路径保持一致）—— 兜底路径不会越过候选集匹配。
/// </summary>
public class KeywordDocumentClassifierTests
{
    private readonly KeywordDocumentClassifier _classifier = new();

    private static DocumentTypeDefinition Type(string typeCode, string displayName, int priority, params string[] keywords)
        => new(typeCode, displayName)
        {
            Priority = priority,
            MatchKeywords = new List<string>(keywords)
        };

    [Fact]
    public void Returns_Null_When_Candidates_Empty()
    {
        var outcome = _classifier.Classify(new List<DocumentTypeDefinition>(), "any text");

        outcome.TypeCode.ShouldBeNull();
        outcome.ConfidenceScore.ShouldBe(0);
    }

    [Fact]
    public void Returns_Null_When_No_Keyword_Match_In_Candidates()
    {
        var candidates = new List<DocumentTypeDefinition>
        {
            Type("contract.general", "合同", 10, "契約書")
        };

        var outcome = _classifier.Classify(candidates, "This is a quarterly report.");

        outcome.TypeCode.ShouldBeNull();
        outcome.ConfidenceScore.ShouldBe(0);
    }

    [Fact]
    public void Returns_Match_With_KeywordMatchConfidence()
    {
        var candidates = new List<DocumentTypeDefinition>
        {
            Type("contract.general", "合同", 10, "契約書")
        };

        var outcome = _classifier.Classify(candidates, "業務委託契約書の内容です。");

        outcome.TypeCode.ShouldBe("contract.general");
        outcome.ConfidenceScore.ShouldBe(ClassificationDefaults.KeywordMatchConfidence);
    }

    [Fact]
    public void Picks_Highest_Priority_When_Multiple_Candidates_Match()
    {
        var candidates = new List<DocumentTypeDefinition>
        {
            Type("contract.nda", "NDA", 5, "契約"),
            Type("contract.general", "合同", 100, "契約")
        };

        var outcome = _classifier.Classify(candidates, "業務委託契約書");

        outcome.TypeCode.ShouldBe("contract.general");
    }

    [Fact]
    public void Does_Not_Match_Types_Outside_Provided_Candidates()
    {
        // 即便 "invoice.general" 的关键词在文本中存在，
        // 候选集只含 contract.general 时也不应越过候选集。
        var candidates = new List<DocumentTypeDefinition>
        {
            Type("contract.general", "合同", 10, "契約書")
        };

        var outcome = _classifier.Classify(candidates, "請求書 invoice");

        outcome.TypeCode.ShouldBeNull();
    }
}
