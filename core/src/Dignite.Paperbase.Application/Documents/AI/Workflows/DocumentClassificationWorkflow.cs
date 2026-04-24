using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.AI.Workflows;

/// <summary>
/// 文档分类 Workflow（MAF ChatClientAgent + 结构化输出）。
/// </summary>
public class DocumentClassificationWorkflow : ITransientDependency
{
    private const string SystemInstructions =
        "あなたは文書分類の専門家です。" +
        "文書テキストを分析し、最も適合する文書タイプをJSONで回答してください。" +
        "確信が持てない場合は confidence を低く設定し、typeCode は null にしてください。";

    private readonly IChatClient _chatClient;
    private readonly PaperbaseAIOptions _options;

    public DocumentClassificationWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
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

        var trimmedTypes = candidateTypes
            .OrderByDescending(t => t.Priority)
            .Take(_options.MaxDocumentTypesInClassificationPrompt)
            .ToList();

        var maxTextLength = _options.MaxDocumentTypesInClassificationPrompt * 40;
        var truncatedText = extractedText.Length > maxTextLength
            ? extractedText[..maxTextLength]
            : extractedText;

        var typeDescriptions = trimmedTypes.Select(t =>
            $"- TypeCode: {t.TypeCode}\n" +
            $"  Name: {t.DisplayName}" +
            (t.MatchKeywords.Count > 0
                ? $"\n  Keywords: {string.Join(", ", t.MatchKeywords)}"
                : string.Empty));

        var userMessage = $$"""
                ## 登録済み文書タイプ一覧
                {{string.Join("\n", typeDescriptions)}}

                ## 分類対象文書（先頭{{maxTextLength}}文字）
                {{truncatedText}}

                ## 回答形式（JSON のみ、説明不要）
                {
                  "typeCode": "最も適合するTypeCode、該当なしの場合は null",
                  "confidence": 0.0から1.0の数値,
                  "reason": "判定根拠を一文で",
                  "candidates": [
                    {"typeCode": "候補TypeCode", "confidence": 0.0から1.0}
                  ]
                }
                """;

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: SystemInstructions);

        var run = await agent.RunAsync(
            userMessage,
            session: null,
            new ChatClientAgentRunOptions(new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json
            }),
            cancellationToken);

        ClassificationResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<ClassificationResponse>(
                run.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // JSON 解析失败视为低置信度，由调用方处理
        }

        var outcome = new DocumentClassificationOutcome
        {
            TypeCode = parsed?.TypeCode,
            ConfidenceScore = parsed?.Confidence ?? 0,
            Reason = parsed?.Reason
        };

        if (parsed?.Candidates != null)
        {
            foreach (var c in parsed.Candidates)
            {
                outcome.Candidates.Add(new TypeCandidateOutcome
                {
                    TypeCode = c.TypeCode,
                    ConfidenceScore = c.Confidence
                });
            }
        }

        return outcome;
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
