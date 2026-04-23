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

namespace Dignite.Paperbase.AI.Extraction;

/// <summary>
/// AI 结构化字段提取器。实现 IFieldExtractor。
/// 收文本 + FieldSchema 列表，返回字段字典（值均为字符串，业务模块自行解析类型）。
/// </summary>
public class AiFieldExtractor : IFieldExtractor, ITransientDependency
{
    private readonly AuditedChatClient _chatClient;
    private readonly IAmbientAiCallContext _callContext;
    private readonly IAiRunMetadataAccumulator _accumulator;
    private readonly PaperbaseAIOptions _options;

    public AiFieldExtractor(
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

    public virtual async Task<FieldExtractionResult> ExtractAsync(
        FieldExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        _accumulator.Clear();

        using var _ = _callContext.Enter(
            ExtractionPrompts.KeyGenericV1,
            ExtractionPrompts.GenericV1Version);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ExtractionPrompts.GenericV1System),
            new(ChatRole.User, ExtractionPrompts.BuildGenericV1User(
                request.Fields,
                request.ExtractedText,
                _options.MaxTextLengthPerExtraction))
        };

        var response = await _chatClient.GetResponseAsync(
            messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            cancellationToken);

        Dictionary<string, string?>? fields = null;
        try
        {
            fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                response.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // JSON 解析失败，返回空字典，业务模块触发人工录入
        }

        var result = new FieldExtractionResult
        {
            Fields = fields ?? new Dictionary<string, string?>(),
        };

        var auditMeta = _accumulator.ToDictionary();
        foreach (var kv in auditMeta)
            result.Metadata[kv.Key] = kv.Value;

        return result;
    }
}
