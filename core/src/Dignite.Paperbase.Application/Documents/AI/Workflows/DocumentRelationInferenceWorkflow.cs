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
    private readonly IChatClient _chatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly PaperbaseAIOptions _options;

    public ILogger<DocumentRelationInferenceWorkflow> Logger { get; set; }
        = NullLogger<DocumentRelationInferenceWorkflow>.Instance;

    public DocumentRelationInferenceWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options,
        IPromptProvider promptProvider)
    {
        _chatClient = chatClient;
        _promptProvider = promptProvider;
        _options = options.Value;
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

        var template = _promptProvider.GetRelationInferencePrompt(
            _options.DefaultLanguage, _options.RelationInferenceMinConfidence);
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: template.SystemInstructions + " " + PromptBoundary.BoundaryRule);

        var response = await agent.RunAsync<List<RelationItem>>(
            userMessage,
            session: null,
            serializerOptions: null,
            options: null,
            cancellationToken);

        var results = new List<InferredDocumentRelation>();
        var items = response.Result;
        var droppedInvalidGuid = 0;
        var droppedEmptyDescription = 0;
        var clampedConfidence = 0;

        if (items != null)
        {
            foreach (var item in items)
            {
                if (!Guid.TryParse(item.TargetDocumentId, out var targetId))
                {
                    droppedInvalidGuid++;
                    continue;
                }

                var description = item.Description?.Trim();
                if (string.IsNullOrEmpty(description))
                {
                    droppedEmptyDescription++;
                    continue;
                }

                var confidence = item.Confidence;
                if (double.IsNaN(confidence) || confidence < 0d || confidence > 1d)
                {
                    // Clamp 而非丢弃：description 已经成立时，置信度的"漂移"不应导致条目消失。
                    clampedConfidence++;
                    confidence = double.IsNaN(confidence) ? 0d : Math.Clamp(confidence, 0d, 1d);
                }

                results.Add(new InferredDocumentRelation
                {
                    TargetDocumentId = targetId,
                    Description = description,
                    Confidence = confidence
                });
            }
        }

        if (droppedInvalidGuid + droppedEmptyDescription + clampedConfidence > 0)
        {
            Logger.LogWarning(
                "Relation inference parsing for source {SourceDocumentId}: dropped {InvalidGuid} for invalid GUID, {EmptyDescription} for empty description; clamped {Clamped} out-of-range confidence values.",
                sourceDocumentId, droppedInvalidGuid, droppedEmptyDescription, clampedConfidence);
        }

        return results;
    }

    protected virtual string BuildUserMessage(
        string sourceText,
        IReadOnlyList<RelationCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source document excerpt:");
        var truncated = sourceText;
        if (sourceText.Length > _options.MaxTextLengthPerExtraction)
        {
            Logger.LogWarning(
                "Relation inference source text truncated from {OriginalLength} to {TruncatedLength} characters.",
                sourceText.Length, _options.MaxTextLengthPerExtraction);
            truncated = sourceText[.._options.MaxTextLengthPerExtraction] + "...";
        }
        sb.AppendLine(PromptBoundary.WrapDocument(truncated));
        sb.AppendLine();
        sb.AppendLine("Candidate documents:");

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            sb.AppendLine($"- id: {candidate.DocumentId}, type: {candidate.DocumentTypeCode ?? "unknown"}");
            sb.AppendLine($"  excerpt: {PromptBoundary.WrapCandidate(i, candidate.Summary)}");
        }

        return sb.ToString();
    }

    private sealed class RelationItem
    {
        public string? TargetDocumentId { get; set; }
        public string? Description { get; set; }
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
    public string Description { get; set; } = default!;
    public double Confidence { get; set; }
}
