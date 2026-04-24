using System.Collections.Generic;
using System.Linq;
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
        "You are a document classification expert. " +
        "Analyze the document text and determine the best matching document type from the provided list. " +
        "If you are not confident, set confidence low and typeCode to null.";

    private readonly ChatClientAgent _agent;
    private readonly PaperbaseAIOptions _options;

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

        var trimmedTypes = candidateTypes
            .OrderByDescending(t => t.Priority)
            .Take(_options.MaxDocumentTypesInClassificationPrompt)
            .ToList();

        var truncatedText = extractedText.Length > _options.MaxTextLengthPerExtraction
            ? extractedText[.._options.MaxTextLengthPerExtraction]
            : extractedText;

        var typeDescriptions = trimmedTypes.Select(t =>
            $"- TypeCode: {t.TypeCode}\n" +
            $"  Name: {t.DisplayName}" +
            (t.MatchKeywords.Count > 0
                ? $"\n  Keywords: {string.Join(", ", t.MatchKeywords)}"
                : string.Empty));

        var userMessage = $$"""
                ## Registered Document Types
                {{string.Join("\n", typeDescriptions)}}

                ## Document Text (first {{_options.MaxTextLengthPerExtraction}} characters)
                {{truncatedText}}

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
