using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI.Audit;
using Dignite.Paperbase.AI.Prompts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.QA;

public class AiQaService : IQaService, ITransientDependency
{
    private readonly AuditedChatClient _chatClient;
    private readonly IAmbientAiCallContext _callContext;
    private readonly IAiRunMetadataAccumulator _accumulator;
    private readonly PaperbaseAIOptions _options;

    public AiQaService(
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

    public virtual async Task<QaResult> AskAsync(
        QaRequest request,
        CancellationToken cancellationToken = default)
    {
        var actualMode = DetermineActualMode(request);

        return actualMode == QaMode.FullText
            ? await AskFullTextAsync(request, cancellationToken)
            : await AskRagAsync(request, actualMode, cancellationToken);
    }

    private QaMode DetermineActualMode(QaRequest request)
    {
        if (request.Mode == QaMode.Rag) return QaMode.Rag;
        if (request.Mode == QaMode.FullText) return QaMode.FullText;
        return request.HasEmbedding && request.Chunks.Count > 0 ? QaMode.Rag : QaMode.FullText;
    }

    private async Task<QaResult> AskRagAsync(
        QaRequest request, QaMode actualMode, CancellationToken cancellationToken)
    {
        _accumulator.Clear();
        using var _ = _callContext.Enter(QaPrompts.KeyDocumentQaV1, QaPrompts.DocumentQaV1Version);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, QaPrompts.DocumentQaV1System),
            new(ChatRole.User, QaPrompts.BuildRagUser(request.Question, request.Chunks))
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        var result = new QaResult
        {
            Answer = response.Text,
            ActualMode = actualMode
        };

        foreach (var chunk in request.Chunks)
        {
            if (response.Text.Contains($"[chunk {chunk.ChunkIndex}]"))
            {
                result.Sources.Add(new QaSource
                {
                    Text = chunk.ChunkText[..System.Math.Min(200, chunk.ChunkText.Length)],
                    ChunkIndex = chunk.ChunkIndex
                });
            }
        }

        var auditMeta = _accumulator.ToDictionary();
        foreach (var kv in auditMeta)
            result.Metadata[kv.Key] = kv.Value;

        return result;
    }

    private async Task<QaResult> AskFullTextAsync(
        QaRequest request, CancellationToken cancellationToken)
    {
        _accumulator.Clear();
        using var _ = _callContext.Enter(QaPrompts.KeyDocumentQaV1, QaPrompts.DocumentQaV1Version);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, QaPrompts.DocumentQaV1System),
            new(ChatRole.User, QaPrompts.BuildFullTextUser(
                request.Question, request.ExtractedText, _options.MaxTextLengthPerExtraction))
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        var result = new QaResult
        {
            Answer = response.Text,
            ActualMode = QaMode.FullText
        };

        var auditMeta = _accumulator.ToDictionary();
        foreach (var kv in auditMeta)
            result.Metadata[kv.Key] = kv.Value;

        return result;
    }
}
