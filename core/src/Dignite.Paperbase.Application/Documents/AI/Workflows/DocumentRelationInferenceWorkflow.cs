using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.AI.Workflows;

/// <summary>
/// 关系推断 Workflow：基于源文档与候选摘要，调用 LLM 推断结构化关系列表。
/// </summary>
public class DocumentRelationInferenceWorkflow : ITransientDependency
{
    private const string SystemInstructions =
        "You are a document relationship analyst. Given a source document and a list of candidate documents, " +
        "identify which candidates have a meaningful relationship with the source. " +
        "Relation types: " +
        "\"supplements\" (adds information to), " +
        "\"supersedes\" (replaces or amends), " +
        "\"belongs-to\" (attachment or sub-document of), " +
        "\"related-to\" (general relevance). " +
        "Return a JSON array. Each item must have: targetDocumentId (string), relationType (string), confidence (0.0-1.0). " +
        "Only include pairs with confidence >= 0.5. Return [] if none qualify.";

    private readonly ChatClientAgent _agent;
    private readonly PaperbaseAIOptions _options;

    public ILogger<DocumentRelationInferenceWorkflow> Logger { get; set; }
        = NullLogger<DocumentRelationInferenceWorkflow>.Instance;

    public DocumentRelationInferenceWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options)
    {
        _options = options.Value;
        _agent = new ChatClientAgent(chatClient, instructions: SystemInstructions);
    }

    public virtual async Task<IReadOnlyList<InferredDocumentRelation>> RunAsync(
        Guid sourceDocumentId,
        string sourceText,
        IReadOnlyList<RelationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return [];

        var userMessage = BuildUserMessage(sourceText, candidates);

        var response = await _agent.RunAsync<List<RelationItem>>(
            userMessage,
            session: null,
            serializerOptions: null,
            options: null,
            cancellationToken);

        var results = new List<InferredDocumentRelation>();
        var items = response.Result;

        if (items != null)
        {
            foreach (var item in items)
            {
                if (Guid.TryParse(item.TargetDocumentId, out var targetId)
                    && !string.IsNullOrEmpty(item.RelationType))
                {
                    results.Add(new InferredDocumentRelation
                    {
                        TargetDocumentId = targetId,
                        RelationType = item.RelationType,
                        Confidence = item.Confidence
                    });
                }
            }
        }

        return results;
    }

    protected virtual string BuildUserMessage(
        string sourceText,
        IReadOnlyList<RelationCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source document excerpt:");
        var truncated = sourceText.Length > _options.MaxTextLengthPerExtraction
            ? sourceText[.._options.MaxTextLengthPerExtraction] + "..."
            : sourceText;
        sb.AppendLine(truncated);
        sb.AppendLine();
        sb.AppendLine("Candidate documents:");

        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- id: {candidate.DocumentId}, type: {candidate.DocumentTypeCode ?? "unknown"}");
            sb.AppendLine($"  excerpt: {candidate.Summary}");
        }

        return sb.ToString();
    }

    private sealed class RelationItem
    {
        public string? TargetDocumentId { get; set; }
        public string? RelationType { get; set; }
        public double Confidence { get; set; }
    }
}

public class RelationCandidate
{
    public Guid DocumentId { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string Summary { get; set; } = default!;
}

public class InferredDocumentRelation
{
    public Guid TargetDocumentId { get; set; }
    public string RelationType { get; set; } = default!;
    public double Confidence { get; set; }
}
