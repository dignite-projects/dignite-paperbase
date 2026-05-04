using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Ai;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Chat.Search;

/// <summary>
/// LLM 精排 Workflow（MAF ChatClientAgent + 结构化输出）。
/// 输入向量召回得到的扩大候选集，让 LLM 按"能否直接回答问题"打分（0-1），
/// 再按分数排序取前 N。失败时优雅降级，保持原向量距离顺序。
/// </summary>
public class DocumentRerankWorkflow : ITransientDependency
{
    private readonly IChatClient _chatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly PaperbaseAIBehaviorOptions _options;

    public ILogger<DocumentRerankWorkflow> Logger { get; set; }
        = NullLogger<DocumentRerankWorkflow>.Instance;

    public DocumentRerankWorkflow(
        IChatClient chatClient,
        IOptions<PaperbaseAIBehaviorOptions> options,
        IPromptProvider promptProvider)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _promptProvider = promptProvider;
    }

    /// <summary>
    /// 对 <paramref name="candidates"/> 重新打分排序并取前 <paramref name="topK"/>。
    /// 当候选数 ≤ topK 时直接返回原列表，避免无意义的 LLM 调用。
    /// LLM 异常或输出解析失败时按原顺序截取。
    /// </summary>
    public virtual async Task<IReadOnlyList<RerankedChunk>> RerankAsync(
        string question,
        IReadOnlyList<RerankCandidate> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0 || candidates.Count == 0)
            return [];

        if (candidates.Count <= topK)
        {
            return candidates
                .Select((c, i) => new RerankedChunk(c, c.OriginalScore, i))
                .ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Question: {PromptBoundary.WrapQuestion(question)}");
        sb.AppendLine();
        sb.AppendLine("Candidate passages:");
        for (var i = 0; i < candidates.Count; i++)
        {
            sb.AppendLine($"[id={i}]");
            sb.AppendLine(PromptBoundary.WrapDocument(candidates[i].Text));
            sb.AppendLine();
        }
        sb.AppendLine("Score every passage above.");

        var template = _promptProvider.GetRerankPrompt(_options.DefaultLanguage);
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: template.SystemInstructions + " " + PromptBoundary.BoundaryRule);

        try
        {
            var response = await agent.RunAsync<RerankResponse>(
                sb.ToString(),
                session: null,
                serializerOptions: null,
                options: BuildRunOptions(_options.UseStrictJsonMode),
                cancellationToken);

            return SelectTopK(candidates, response.Result?.Items, topK);
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            Logger.LogWarning(ex,
                "LLM rerank failed; falling back to vector-distance order for {Count} candidates.",
                candidates.Count);
            return candidates
                .Take(topK)
                .Select((c, i) => new RerankedChunk(c, c.OriginalScore, i))
                .ToList();
        }
    }

    protected virtual IReadOnlyList<RerankedChunk> SelectTopK(
        IReadOnlyList<RerankCandidate> candidates,
        IReadOnlyList<RerankResponse.Item>? items,
        int topK)
    {
        if (items == null || items.Count == 0)
        {
            Logger.LogWarning(
                "LLM rerank returned no items for {Count} candidates; falling back to vector-distance order.",
                candidates.Count);
            return candidates
                .Take(topK)
                .Select((c, i) => new RerankedChunk(c, c.OriginalScore, i))
                .ToList();
        }

        var byId = items
            .Where(it => it.Id >= 0 && it.Id < candidates.Count)
            .GroupBy(it => it.Id)
            .ToDictionary(g => g.Key, g => ClampScore(g.First().Score));

        return candidates
            .Select((c, idx) => (Candidate: c, Index: idx, Score: byId.GetValueOrDefault(idx, 0d)))
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Index)
            .Take(topK)
            .Select(t => new RerankedChunk(t.Candidate, t.Score, t.Index))
            .ToList();
    }

    internal static ChatClientAgentRunOptions? BuildRunOptions(bool useStrictJsonMode)
        => useStrictJsonMode
            ? new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ChatResponseFormat.Json })
            : null;

    internal static double ClampScore(double value)
    {
        if (double.IsNaN(value)) return 0d;
        if (value < 0d) return 0d;
        if (value > 1d) return 1d;
        return value;
    }

    public sealed class RerankResponse
    {
        public List<Item> Items { get; set; } = new();

        public sealed class Item
        {
            public int Id { get; set; }
            public double Score { get; set; }
        }
    }
}

/// <summary>
/// 精排输入项，承载召回阶段的元信息（chunk 引用 + 向量距离），用于 LLM 失败时的降级排序。
/// </summary>
public sealed class RerankCandidate
{
    public string Text { get; }
    public double OriginalScore { get; }
    public object? Tag { get; }

    public RerankCandidate(string text, double originalScore, object? tag = null)
    {
        Text = text;
        OriginalScore = originalScore;
        Tag = tag;
    }
}

/// <summary>
/// 精排输出项，包含原候选、LLM 给出的相关性分数以及在原候选列表中的位置（用于日志/审计）。
/// </summary>
public sealed class RerankedChunk
{
    public RerankCandidate Candidate { get; }
    public double Score { get; }
    public int OriginalIndex { get; }

    public RerankedChunk(RerankCandidate candidate, double score, int originalIndex)
    {
        Candidate = candidate;
        Score = score;
        OriginalIndex = originalIndex;
    }
}
