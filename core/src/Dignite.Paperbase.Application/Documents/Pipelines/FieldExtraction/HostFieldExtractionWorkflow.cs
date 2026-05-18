using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.FieldExtraction;

/// <summary>
/// Host 字段抽取工作流——按 <see cref="HostFieldDefinition"/> 列表用 LLM 单次调用提取字段值。
/// <para>
/// 设计要点：
/// <list type="bullet">
///   <item>所有字段一次调用提取，减少 LLM 往返 + 上下文重复</item>
///   <item>用 <c>ChatResponseFormat.Json</c> 限定输出为 JSON，prompt 内动态描述每个字段的 name/prompt/dataType</item>
///   <item>解析结果时按 <see cref="HostFieldDefinition.DataType"/> 做类型转换（容错：转换失败的字段写 null + log）</item>
///   <item>整个 workflow 共享 <see cref="PaperbaseAIConsts.StructuredChatClientKey"/> chat client</item>
/// </list>
/// </para>
/// </summary>
public class HostFieldExtractionWorkflow : ITransientDependency
{
    private readonly IChatClient _chatClient;
    private readonly PaperbaseAIBehaviorOptions _behaviorOptions;
    private readonly ILogger<HostFieldExtractionWorkflow> _logger;

    public HostFieldExtractionWorkflow(
        [FromKeyedServices(PaperbaseAIConsts.StructuredChatClientKey)] IChatClient chatClient,
        IOptions<PaperbaseAIBehaviorOptions> behaviorOptions,
        ILogger<HostFieldExtractionWorkflow> logger)
    {
        _chatClient = chatClient;
        _behaviorOptions = behaviorOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 按字段定义批量抽取。<paramref name="markdown"/> 已由调用方截断到合理长度（参考
    /// <see cref="PaperbaseAIBehaviorOptions.MaxTextLengthPerExtraction"/>）。
    /// 返回 (字段名 → 提取值) 字典；缺失/无法解析的字段以 null 形式出现，方便调用方判断完整性。
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, object?>> ExtractAsync(
        IReadOnlyList<HostFieldDefinition> fields,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var truncated = markdown.Length > _behaviorOptions.MaxTextLengthPerExtraction
            ? markdown[.._behaviorOptions.MaxTextLengthPerExtraction]
            : markdown;

        var systemPrompt = BuildSystemPrompt(fields);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt + "\n\n" + PromptBoundary.BoundaryRule),
            new(ChatRole.User, PromptBoundary.WrapDocument(truncated))
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        var rawJson = response.Text?.Trim() ?? string.Empty;

        return ParseJsonToTypedDictionary(rawJson, fields);
    }

    private static string BuildSystemPrompt(IReadOnlyList<HostFieldDefinition> fields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You extract structured fields from a Markdown document. ");
        sb.AppendLine("Return JSON only with one key per requested field. ");
        sb.AppendLine("When a field cannot be confidently extracted, set its value to null. ");
        sb.AppendLine("The input document is provided as Markdown — treat headings, tables, and lists as semantic structure signals.");
        sb.AppendLine();
        sb.AppendLine("Fields to extract:");
        foreach (var f in fields)
        {
            // f.Prompt is admin/tenant-supplied free-form text — wrap with PromptBoundary
            // so downstream BoundaryRule treats it as data, never instructions.
            // f.Name is a JSON key (used to parse the response), validated at domain layer
            // to be alphanumeric/identifier-shaped; safe to render unwrapped.
            sb.AppendLine($"- \"{f.Name}\" ({f.DataType}, {(f.Required ? "required" : "optional")}): {PromptBoundary.WrapField(f.Prompt)}");
        }
        return sb.ToString();
    }

    private IReadOnlyDictionary<string, object?> ParseJsonToTypedDictionary(
        string rawJson,
        IReadOnlyList<HostFieldDefinition> fields)
    {
        var result = new Dictionary<string, object?>(fields.Count);

        // 容错：响应为空/非 JSON 时全部字段返回 null
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            foreach (var f in fields) result[f.Name] = null;
            return result;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Host field extraction returned non-JSON output: {Raw}", rawJson);
            foreach (var f in fields) result[f.Name] = null;
            return result;
        }

        foreach (var field in fields)
        {
            if (!root.TryGetProperty(field.Name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            {
                result[field.Name] = null;
                continue;
            }

            result[field.Name] = CoerceValue(prop, field.DataType, field.Name);
        }

        return result;
    }

    private object? CoerceValue(JsonElement element, FieldDataType dataType, string fieldName)
    {
        try
        {
            return dataType switch
            {
                FieldDataType.String => element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString(),
                FieldDataType.Integer => element.ValueKind switch
                {
                    JsonValueKind.Number => element.GetInt64(),
                    JsonValueKind.String when long.TryParse(element.GetString(), out var l) => l,
                    _ => null
                },
                FieldDataType.Decimal => element.ValueKind switch
                {
                    JsonValueKind.Number => element.GetDecimal(),
                    JsonValueKind.String when decimal.TryParse(element.GetString(), out var d) => d,
                    _ => null
                },
                FieldDataType.Boolean => element.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(element.GetString(), out var b) => b,
                    _ => null
                },
                FieldDataType.Date or FieldDataType.DateTime => element.ValueKind == JsonValueKind.String
                    ? (DateTime.TryParse(element.GetString(), out var dt) ? dt : null)
                    : null,
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Field '{Field}' value coercion to {Type} failed; raw element: {Raw}",
                fieldName, dataType, element.ToString());
            return null;
        }
    }
}
