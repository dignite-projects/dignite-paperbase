using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.AI.Workflows;

/// <summary>
/// 文档问答 Workflow：基于 RAG 检索块或全文上下文回答问题。
/// </summary>
public class DocumentQaWorkflow : ITransientDependency
{
    private const string SystemInstructions =
        "You are a helpful assistant that answers questions based on the provided document content. " +
        "Answer in the same language as the question. " +
        "If citing a source, reference it by [chunk N]. " +
        "If the answer is not in the provided content, say so clearly rather than guessing.";

    private readonly IChatClient _chatClient;
    private readonly PaperbaseAIOptions _options;

    public DocumentQaWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIOptions> options)
    {
        _chatClient = chatClient;
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
            sb.AppendLine($"[chunk {chunk.ChunkIndex}]");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {question}");

        return InvokeAsync(sb.ToString(), QaMode.Rag, chunks, cancellationToken);
    }

    public virtual Task<DocumentQaOutcome> RunFullTextAsync(
        string question,
        string? extractedText,
        CancellationToken cancellationToken = default)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length > _options.MaxTextLengthPerExtraction)
            text = text[.._options.MaxTextLengthPerExtraction] + "\n[... document truncated ...]";

        var prompt = $"Document content:\n{text}\n\nQuestion: {question}";
        return InvokeAsync(prompt, QaMode.FullText, [], cancellationToken);
    }

    protected virtual async Task<DocumentQaOutcome> InvokeAsync(
        string userMessage,
        QaMode mode,
        IReadOnlyList<QaChunk> chunks,
        CancellationToken cancellationToken)
    {
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: SystemInstructions);

        var run = await agent.RunAsync(userMessage, session: null, options: null, cancellationToken);

        var outcome = new DocumentQaOutcome
        {
            Answer = run.Text,
            ActualMode = mode
        };

        foreach (var chunk in chunks)
        {
            if (run.Text.Contains($"[chunk {chunk.ChunkIndex}]"))
            {
                outcome.Sources.Add(new QaSourceItem
                {
                    Text = chunk.ChunkText[..System.Math.Min(200, chunk.ChunkText.Length)],
                    ChunkIndex = chunk.ChunkIndex
                });
            }
        }

        return outcome;
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
