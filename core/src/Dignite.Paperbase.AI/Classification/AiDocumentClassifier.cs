using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI.Audit;
using Dignite.Paperbase.AI.Prompts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Classification;

/// <summary>
/// AI 文档分类器。实现 IDocumentClassifier，替换 KeywordDocumentClassifier。
/// 收 ClassificationRequest（ExtractedText + 候选类型），返回 TypeCode + ConfidenceScore + Candidates。
/// 阈值判断和 LowConfidence 处理由核心模块 DocumentClassificationBackgroundJob 负责。
/// </summary>
public class AiDocumentClassifier : IDocumentClassifier, ITransientDependency
{
    private readonly AuditedChatClient _chatClient;
    private readonly IAmbientAiCallContext _callContext;
    private readonly IAiRunMetadataAccumulator _accumulator;
    private readonly PaperbaseAIOptions _options;

    public AiDocumentClassifier(
        AuditedChatClient chatClient,
        IAmbientAiCallContext callContext,
        IAiRunMetadataAccumulator accumulator,
        IOptions<PaperbaseAIOptions> options)
    {
        _chatClient = chatClient;
        _callContext = callContext;
        _accumulator = accumulator;
        _options = options.Value;
    }

    public virtual async Task<ClassificationResult> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CandidateTypes == null || request.CandidateTypes.Count == 0)
        {
            return new ClassificationResult
            {
                TypeCode = null,
                ConfidenceScore = 0,
                Metadata = { ["Reason"] = "No candidate types provided." }
            };
        }

        _accumulator.Clear();

        using var _ = _callContext.Enter(
            ClassificationPrompts.KeyDocumentClassificationV1,
            ClassificationPrompts.DocumentClassificationV1Version);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ClassificationPrompts.DocumentClassificationV1System),
            new(ChatRole.User, ClassificationPrompts.BuildDocumentClassificationV1User(
                request.CandidateTypes,
                request.ExtractedText,
                _options.MaxDocumentTypesInClassificationPrompt * 40))
        };

        var response = await _chatClient.GetResponseAsync(
            messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            cancellationToken);

        ClassificationResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<ClassificationResponse>(
                response.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // JSON 解析失败视为 LowConfidence，由调用方处理
        }

        if (parsed != null)
            _callContext.OutputConfidence = parsed.Confidence;

        var result = new ClassificationResult
        {
            TypeCode = parsed?.TypeCode,
            ConfidenceScore = parsed?.Confidence ?? 0,
        };

        // 把 Top-K 候选注入 Candidates（供 LowConfidence UI 使用）
        if (parsed?.Candidates != null)
        {
            foreach (var c in parsed.Candidates)
                result.Candidates.Add(new TypeCandidate { TypeCode = c.TypeCode, ConfidenceScore = c.Confidence });
        }

        // 合并审计 Metadata
        var auditMeta = _accumulator.ToDictionary();
        foreach (var kv in auditMeta)
            result.Metadata[kv.Key] = kv.Value;
        if (parsed?.Reason != null)
            result.Metadata["Reason"] = parsed.Reason;

        return result;
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
