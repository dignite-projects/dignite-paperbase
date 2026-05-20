using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.FieldExtraction;

/// <summary>
/// 统一字段抽取工作流（字段架构 v2）。按 <see cref="FieldExtractionDescriptor"/> 列表用 LLM
/// 单次调用提取字段值——不区分 Host 字段 / 租户字段（来源由调用方决定）。
/// <para>
/// 设计要点：
/// <list type="bullet">
///   <item>所有字段一次调用提取，减少 LLM 往返 + 上下文重复</item>
///   <item>用 <c>ChatResponseFormat.Json</c> 限定输出为 JSON</item>
///   <item>解析时按 <see cref="FieldDataType"/> 做类型转换（容错：转换失败的字段写 null + log）</item>
///   <item>所有字段的 prompt（包括 Host 来源）统一经 <c>PromptBoundary.WrapField</c> 包裹——
///         比 v1 区分 Host/Tenant 是否 wrap 更保守，无功能损失</item>
/// </list>
/// </para>
/// </summary>
public class FieldExtractionWorkflow : ITransientDependency
{
    private readonly IChatClient _chatClient;
    private readonly PaperbaseAIBehaviorOptions _behaviorOptions;
    private readonly ILogger<FieldExtractionWorkflow> _logger;

    public FieldExtractionWorkflow(
        [FromKeyedServices(PaperbaseAIConsts.StructuredChatClientKey)] IChatClient chatClient,
        IOptions<PaperbaseAIBehaviorOptions> behaviorOptions,
        ILogger<FieldExtractionWorkflow> logger)
    {
        _chatClient = chatClient;
        _behaviorOptions = behaviorOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 按字段定义批量抽取。返回 (字段名 → JsonElement) 字典；缺失/无法解析的字段以 null 形式出现。
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, JsonElement?>> ExtractAsync(
        IReadOnlyList<FieldExtractionDescriptor> fields,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0)
        {
            return new Dictionary<string, JsonElement?>();
        }

        var truncated = markdown.Length > _behaviorOptions.MaxTextLengthPerExtraction
            ? markdown[.._behaviorOptions.MaxTextLengthPerExtraction]
            : markdown;

        // system role 保持**编译期常量** —— 防 prompt injection（CLAUDE.md "## 安全约定 / Description / Instructions 编译期常量"）。
        // 字段 schema（含租户用户输入的 f.Name / f.Prompt）放进 user role 第一条 message，
        // 让模型把"指令"与"用户数据"分开看待——配合 PromptBoundary.WrapField + BoundaryRule。
        // FieldDefinition.Name 在实体层已强制白名单 [A-Za-z0-9_-]{1,64}（参见 FieldDefinitionConsts.NamePattern），
        // 不会含换行 / 引号 / Markdown 控制字符；f.Prompt 长度受 MaxPromptLength 上限保护。
        var schemaMessage = BuildSchemaUserMessage(fields);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemInstructions + "\n\n" + PromptBoundary.BoundaryRule),
            new(ChatRole.User, schemaMessage),
            new(ChatRole.User, PromptBoundary.WrapDocument(truncated))
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        var rawJson = response.Text?.Trim() ?? string.Empty;

        return ParseJsonToDictionary(rawJson, fields);
    }

    /// <summary>
    /// 编译期常量 system instructions。**不允许**拼接任何运行时字符串。
    /// </summary>
    private const string SystemInstructions =
        "You extract structured fields from a Markdown document. " +
        "The first user message lists the fields to extract (schema). " +
        "The second user message contains the document body. " +
        "Return JSON only with one key per requested field. " +
        "When a field cannot be confidently extracted, set its value to null. " +
        "The input document is provided as Markdown — treat headings, tables, and lists as semantic structure signals.";

    private static string BuildSchemaUserMessage(IReadOnlyList<FieldExtractionDescriptor> fields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Fields to extract:");
        foreach (var f in fields)
        {
            // f.Name 已经在 FieldDefinition 实体层经白名单 regex 校验，仅含 [A-Za-z0-9_-]。
            // f.Prompt 来自 Host 编译期常量或租户用户输入 —— 经 PromptBoundary.WrapField 显式标记为数据，
            // BoundaryRule 让模型把它当指令以外的内容看待。
            sb.AppendLine($"- \"{f.Name}\" ({f.DataType}, {(f.IsRequired ? "required" : "optional")}): {PromptBoundary.WrapField(f.Prompt)}");
        }
        return sb.ToString();
    }

    private IReadOnlyDictionary<string, JsonElement?> ParseJsonToDictionary(
        string rawJson,
        IReadOnlyList<FieldExtractionDescriptor> fields)
    {
        var result = new Dictionary<string, JsonElement?>(fields.Count);

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
            _logger.LogWarning(ex, "Field extraction returned non-JSON output: {Raw}", rawJson);
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

            // 不在 Workflow 内做类型转换 —— JsonElement 是值的中间形态，
            // 持久化到 Document.ExtractedFields（Dictionary<string, JsonElement>）单桶——
            // 按 Document.TenantId 决定本文档跑哪层 FieldDefinition，结果落同一桶。
            // 消费侧（REST API / 业务模块）按 FieldDefinition.DataType 反序列化。
            // 这与 v1 在 Workflow 内做 typed coercion 不同：保留原始 JsonElement 避免双重转换 + 损失精度。
            result[field.Name] = prop;
        }

        return result;
    }
}
