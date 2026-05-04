using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// A per-turn <see cref="IChatClient"/> wrapper that enforces a maximum number of
/// tool-call rounds within a single agent turn, preventing runaway LLM tool loops.
///
/// <para>
/// Create a new instance for every chat turn (via <c>PrepareAgentSetupAsync</c>).
/// The internal counter is NOT thread-safe — each instance is used by exactly one
/// concurrent <c>RunAsync</c> / <c>RunStreamingAsync</c> call.
/// </para>
///
/// <para>
/// When the limit is reached, tools are stripped from <see cref="ChatOptions"/> on the
/// next completion request, which forces the model to produce a final answer rather than
/// issuing another tool call. The limit is controlled by
/// <c>PaperbaseAIBehaviorOptions.MaxToolCallsPerTurn</c>; a value of 0 disables the cap.
/// </para>
///
/// <para>
/// All tool invocations are logged at Information level with the function name,
/// serialized arguments, cumulative call count, and round-trip latency.
/// </para>
/// </summary>
internal sealed class MaxToolCallsChatClient : DelegatingChatClient
{
    private readonly int _maxToolCalls;
    private readonly ILogger _logger;
    private int _callCount;

    public MaxToolCallsChatClient(IChatClient innerClient, int maxToolCalls, ILogger logger)
        : base(innerClient)
    {
        _maxToolCalls = maxToolCalls;
        _logger = logger;
        _callCount = 0;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = StripToolsIfLimitReached(options);

        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, effectiveOptions, cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();

        LogToolCalls(response.Messages, sw.ElapsedMilliseconds);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveOptions = StripToolsIfLimitReached(options);

        await foreach (var update in base.GetStreamingResponseAsync(messages, effectiveOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private ChatOptions? StripToolsIfLimitReached(ChatOptions? options)
    {
        if (_maxToolCalls <= 0 || options?.Tools == null || options.Tools.Count == 0)
            return options;

        if (_callCount < _maxToolCalls)
            return options;

        _logger.LogWarning(
            "doc-chat tool-call limit reached ({Limit}); stripping tools to force final answer",
            _maxToolCalls);

        return BuildOptionsWithoutTools(options);
    }

    private void LogToolCalls(IList<AiChatMessage> messages, long elapsedMs)
    {
        var toolCalls = messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        if (toolCalls.Count == 0)
            return;

        _callCount += toolCalls.Count;
        foreach (var call in toolCalls)
        {
            _logger.LogInformation(
                "doc-chat tool call [{Name}] args={Args} cumulative={Cumulative} latency={Latency}ms",
                call.Name,
                SerializeArguments(call.Arguments),
                _callCount,
                elapsedMs);
        }
    }

    private static string SerializeArguments(IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0)
            return "{}";
        try
        {
            return JsonSerializer.Serialize(args);
        }
        catch
        {
            return "(unserializable)";
        }
    }

    /// <summary>
    /// Creates a new <see cref="ChatOptions"/> that copies all settings from
    /// <paramref name="source"/> except <c>Tools</c>, which is set to null so the LLM
    /// cannot make further tool calls.
    /// </summary>
    private static ChatOptions BuildOptionsWithoutTools(ChatOptions source)
    {
        var clone = new ChatOptions
        {
            Instructions = source.Instructions,
            Temperature = source.Temperature,
            TopP = source.TopP,
            TopK = source.TopK,
            FrequencyPenalty = source.FrequencyPenalty,
            PresencePenalty = source.PresencePenalty,
            MaxOutputTokens = source.MaxOutputTokens,
            ResponseFormat = source.ResponseFormat,
            Seed = source.Seed,
            StopSequences = source.StopSequences,
            ToolMode = null,  // no tools → tool mode irrelevant
            // Tools intentionally omitted
        };

        if (source.AdditionalProperties is { Count: > 0 })
        {
            clone.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            foreach (var kvp in source.AdditionalProperties)
                clone.AdditionalProperties[kvp.Key] = kvp.Value;
        }

        return clone;
    }
}
