using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.AI.Workflows;

/// <summary>
/// 文档分类 Workflow（MAF ChatClientAgent + 结构化输出）。
/// </summary>
public class DocumentClassificationWorkflow : ITransientDependency
{
    private const string SystemInstructions =
        "You are a document classification expert. " +
        "Analyze the document text and determine the best matching document type from the provided list. " +
        "If you are not confident, set confidence low and typeCode to null. " +
        PromptBoundary.BoundaryRule;

    private readonly ChatClientAgent _agent;
    private readonly PaperbaseAIOptions _options;

    public ILogger<DocumentClassificationWorkflow> Logger { get; set; }
        = NullLogger<DocumentClassificationWorkflow>.Instance;

    public DocumentClassificationWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options)
    {
        _options = options.Value;
        _agent = new ChatClientAgent(chatClient, instructions: SystemInstructions);
    }

    public virtual async Task<DocumentClassificationOutcome> RunAsync(
        IReadOnlyList<DocumentTypeDefinition> candidateTypes,
        string extractedText,
        CancellationToken cancellationToken = default)
    {
        if (candidateTypes == null || candidateTypes.Count == 0)
        {
            return new DocumentClassificationOutcome
            {
                TypeCode = null,
                ConfidenceScore = 0,
                Reason = "No candidate types provided."
            };
        }

        // 候选集排序与数量上限由调用方（DocumentClassificationBackgroundJob）决定，
        // 以保证 LLM 路径与 KeywordDocumentClassifier 兜底路径使用同一组候选。
        var truncatedText = extractedText.Length > _options.MaxTextLengthPerExtraction
            ? extractedText[.._options.MaxTextLengthPerExtraction]
            : extractedText;

        var typeDescriptions = candidateTypes.Select(t =>
            $"- TypeCode: {t.TypeCode}\n" +
            $"  Name: {t.DisplayName}" +
            (t.MatchKeywords.Count > 0
                ? $"\n  Keywords: {string.Join(", ", t.MatchKeywords)}"
                : string.Empty));

        var userMessage = $$"""
                ## Registered Document Types
                {{string.Join("\n", typeDescriptions)}}

                ## Document Text (first {{_options.MaxTextLengthPerExtraction}} characters)
                {{PromptBoundary.WrapDocument(truncatedText)}}

                ## Response Format (JSON only, no explanation)
                {
                  "typeCode": "best matching TypeCode, or null if none",
                  "confidence": <number between 0.0 and 1.0>,
                  "reason": "one sentence explaining the decision",
                  "candidates": [
                    {"typeCode": "TypeCode", "confidence": <number>}
                  ]
                }

                Respond in: {{_options.DefaultLanguage}}
                """;

        var response = await _agent.RunAsync<ClassificationResponse>(
            userMessage,
            session: null,
            serializerOptions: null,
            options: null,
            cancellationToken);

        var parsed = response.Result;

        // LLM 偶发返回越界置信度（NaN / <0 / >1）。按"无可信结论"处理：
        // typeCode 置 null、confidence 置 0，由 BackgroundJob 走 LowConfidence 分支
        // 触发 PendingReview，避免 Document.ApplyAutomaticClassificationResult 的
        // Check.Range 抛异常导致整条 PipelineRun 翻成 Failed。
        var rawConfidence = parsed?.Confidence ?? 0d;
        var typeCode = parsed?.TypeCode;
        if (!IsValidConfidence(rawConfidence))
        {
            Logger.LogWarning(
                "LLM returned out-of-range classification confidence {Confidence} (typeCode={TypeCode}); routing to PendingReview.",
                rawConfidence, typeCode);
            typeCode = null;
            rawConfidence = 0d;
        }

        var outcome = new DocumentClassificationOutcome
        {
            TypeCode = typeCode,
            ConfidenceScore = rawConfidence,
            Reason = parsed?.Reason
        };

        if (parsed?.Candidates != null)
        {
            foreach (var c in parsed.Candidates)
            {
                // 候选项的 confidence 仅用于 UI 展示与 Run 持久化（PipelineRunCandidate 是纯
                // record，不做 Check.Range），越界不会破坏聚合根；这里 Clamp 保证展示侧不出
                // 现 1.5 之类的脏数据。
                outcome.Candidates.Add(new TypeCandidateOutcome
                {
                    TypeCode = c.TypeCode,
                    ConfidenceScore = ClampConfidence(c.Confidence)
                });
            }
        }

        return outcome;
    }

    // internal so Application.Tests can directly verify the regression-critical
    // out-of-range coercion logic (the surrounding 4-line branch in RunAsync is
    // trivially correct given correct helpers).
    internal static bool IsValidConfidence(double value)
        => !double.IsNaN(value) && value >= 0d && value <= 1d;

    internal static double ClampConfidence(double value)
    {
        if (double.IsNaN(value))
            return 0d;
        if (value < 0d) return 0d;
        if (value > 1d) return 1d;
        return value;
    }

    private sealed class ClassificationResponse
    {
        public string? TypeCode { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }
        public List<CandidateItem> Candidates { get; set; } = new();

        public sealed class CandidateItem
        {
            public string TypeCode { get; set; } = default!;
            public double Confidence { get; set; }
        }
    }
}

public class DocumentClassificationOutcome
{
    public string? TypeCode { get; set; }
    public double ConfidenceScore { get; set; }
    public string? Reason { get; set; }
    public List<TypeCandidateOutcome> Candidates { get; } = new();
}

public class TypeCandidateOutcome
{
    public string TypeCode { get; set; } = default!;
    public double ConfidenceScore { get; set; }
}
