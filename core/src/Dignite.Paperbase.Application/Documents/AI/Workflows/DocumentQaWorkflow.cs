using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
/// 文档问答 Workflow：基于 RAG 检索块或全文上下文回答问题。
/// </summary>
public class DocumentQaWorkflow : ITransientDependency
{
    private readonly IChatClient _chatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly PaperbaseAIOptions _options;

    public ILogger<DocumentQaWorkflow> Logger { get; set; }
        = NullLogger<DocumentQaWorkflow>.Instance;

    public DocumentQaWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options,
        IPromptProvider promptProvider)
    {
        _chatClient = chatClient;
        _promptProvider = promptProvider;
        _options = options.Value;
    }

    public virtual Task<DocumentQaOutcome> RunRagAsync(
        string question,
        IReadOnlyList<QaChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Document content:");
        foreach (var chunk in chunks)
        {
            // [chunk N] 仍保留在包裹外，以便模型在引用时仍能输出"[chunk 0]"格式；
            // 文本本身用 <document> 包裹保护。
            sb.AppendLine($"[chunk {chunk.ChunkIndex}]");
            sb.AppendLine(PromptBoundary.WrapDocument(chunk.ChunkText));
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {PromptBoundary.WrapQuestion(question)}");

        return InvokeAsync(sb.ToString(), QaMode.Rag, chunks, cancellationToken);
    }

    public virtual Task<DocumentQaOutcome> RunFullTextAsync(
        string question,
        string? extractedText,
        CancellationToken cancellationToken = default)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length > _options.MaxTextLengthPerExtraction)
        {
            Logger.LogWarning(
                "QA full-text input truncated from {OriginalLength} to {TruncatedLength} characters; answer may miss content beyond the cutoff.",
                text.Length, _options.MaxTextLengthPerExtraction);
            text = text[.._options.MaxTextLengthPerExtraction] + "\n[... document truncated ...]";
        }

        var prompt = $"Document content:\n{PromptBoundary.WrapDocument(text)}\n\nQuestion: {PromptBoundary.WrapQuestion(question)}";
        return InvokeAsync(prompt, QaMode.FullText, [], cancellationToken);
    }

    protected virtual async Task<DocumentQaOutcome> InvokeAsync(
        string userMessage,
        QaMode mode,
        IReadOnlyList<QaChunk> chunks,
        CancellationToken cancellationToken)
    {
        var template = _promptProvider.GetQaPrompt(_options.DefaultLanguage);
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: template.SystemInstructions + " " + PromptBoundary.BoundaryRule);

        var run = await agent.RunAsync(userMessage, session: null, options: null, cancellationToken);

        var outcome = new DocumentQaOutcome
        {
            Answer = run.Text,
            ActualMode = mode
        };

        var citedIndices = ParseCitedChunkIndices(run.Text);
        foreach (var chunk in chunks)
        {
            if (citedIndices.Contains(chunk.ChunkIndex))
            {
                outcome.Sources.Add(new QaSourceItem
                {
                    Text = chunk.ChunkText[..Math.Min(200, chunk.ChunkText.Length)],
                    ChunkIndex = chunk.ChunkIndex
                });
            }
        }

        return outcome;
    }

    // Tolerates uppercase, multiple spaces, and full-width brackets produced by some LLMs.
    private static readonly Regex CitationPattern =
        new(@"[\[\【]chunk\s*(\d+)[\]\】]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected virtual HashSet<int> ParseCitedChunkIndices(string text)
    {
        var indices = new HashSet<int>();
        foreach (Match m in CitationPattern.Matches(text))
            indices.Add(int.Parse(m.Groups[1].Value));
        return indices;
    }
}

public class QaChunk
{
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = default!;
}

public class DocumentQaOutcome
{
    public string Answer { get; set; } = default!;
    public QaMode ActualMode { get; set; }
    public List<QaSourceItem> Sources { get; } = new();
}

public class QaSourceItem
{
    public string Text { get; set; } = default!;
    public int? ChunkIndex { get; set; }
}
