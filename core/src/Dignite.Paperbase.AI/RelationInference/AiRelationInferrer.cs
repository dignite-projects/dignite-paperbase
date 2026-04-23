using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI.Audit;
using Dignite.Paperbase.AI.Prompts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.RelationInference;

public class AiRelationInferrer : IRelationInferrer, ITransientDependency
{
    private readonly AuditedChatClient _chatClient;
    private readonly IAmbientAiCallContext _callContext;
    private readonly IAiRunMetadataAccumulator _accumulator;

    public ILogger<AiRelationInferrer> Logger { get; set; } = NullLogger<AiRelationInferrer>.Instance;

    public AiRelationInferrer(
        AuditedChatClient chatClient,
        IAmbientAiCallContext callContext,
        IAiRunMetadataAccumulator accumulator)
    {
        _chatClient = chatClient;
        _callContext = callContext;
        _accumulator = accumulator;
    }

    public virtual async Task<IList<InferredRelation>> InferAsync(
        RelationInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Candidates.Count == 0)
            return new List<InferredRelation>();

        _accumulator.Clear();
        using var _ = _callContext.Enter(
            RelationInferencePrompts.KeyRelationInferenceV1,
            RelationInferencePrompts.RelationInferenceV1Version);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, RelationInferencePrompts.RelationInferenceV1System),
            new(ChatRole.User, RelationInferencePrompts.BuildRelationInferenceV1User(
                request.ExtractedText, request.Candidates))
        };

        var response = await _chatClient.GetResponseAsync(
            messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            cancellationToken);

        var results = new List<InferredRelation>();
        try
        {
            var items = JsonSerializer.Deserialize<List<RelationItem>>(
                response.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (Guid.TryParse(item.TargetDocumentId, out var targetId)
                        && !string.IsNullOrEmpty(item.RelationType))
                    {
                        results.Add(new InferredRelation
                        {
                            TargetDocumentId = targetId,
                            RelationType = item.RelationType,
                            Confidence = item.Confidence
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse relation inference response for document {DocumentId}.", request.DocumentId);
        }

        return results;
    }

    private sealed class RelationItem
    {
        public string? TargetDocumentId { get; set; }
        public string? RelationType { get; set; }
        public double Confidence { get; set; }
    }
}
