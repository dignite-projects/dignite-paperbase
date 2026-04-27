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
        "你是文档关系分析师。给定一份源文档和若干候选文档，找出与源文档有实质关系的候选，并用一句中文清楚地说明它们的关系。" +
        "示例说明：'本合同补充了主合同第 3 条付款条款的执行细节'、'替代了 2024-03 版本，原合同作废'、" +
        "'是主合同的附件清单'、'与主合同涉及同一项目'。" +
        "返回一个 JSON 数组，每项包含：targetDocumentId (string)、description (string，中文一句话，不超过 200 字)、confidence (0.0-1.0)。" +
        "仅包含 confidence >= 0.5 的项；若无符合项请返回 []。" +
        PromptBoundary.BoundaryRule;

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
                var description = item.Description?.Trim();
                if (Guid.TryParse(item.TargetDocumentId, out var targetId)
                    && !string.IsNullOrEmpty(description))
                {
                    results.Add(new InferredDocumentRelation
                    {
                        TargetDocumentId = targetId,
                        Description = description,
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
