using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Synthetic <see cref="IDocumentVectorStore"/> for the hybrid-search benchmark.
/// Skips the real embedding / Postgres path entirely so the harness runs without
/// external services.
///
/// Scoring choices, calibrated to model the production pipeline's failure mode:
/// <list type="bullet">
///   <item><b>Vector path</b>: cosine similarity over character-bigram bag-of-words.
///         Captures broad surface overlap — exactly what a dense embedding model
///         tends to do well at, but also tends to under-discriminate when
///         documents share most of their vocabulary and differ only in a rare
///         identifier (e.g., "ABC-001" vs "ABC-002" inside otherwise-identical
///         contract templates). This is the canonical hybrid-wins scenario.</item>
///   <item><b>Keyword path</b>: token-level exact match using the same
///         <c>[\w\-]+</c> tokenization as PostgreSQL's <c>simple</c> regconfig.
///         Score = matched-token-count / query-token-count. Rare IDs ("ABC-001",
///         "INV-2026-04-001") matched intact win; semantic queries with broad
///         vocabulary score evenly across many chunks.</item>
///   <item><b>Hybrid path</b>: each path recalls TopK × 2 candidates, then
///         <see cref="RrfFusion"/> merges the rankings. The harness uses the
///         same <see cref="RrfFusion"/> code as the production
///         <c>PgvectorDocumentVectorStore</c>, so the math under test is
///         identical.</item>
/// </list>
///
/// What this <em>doesn't</em> simulate:
/// real Embedding model behavior (token-level instead of dimensional), real
/// PostgreSQL ts_rank_cd (no IDF weighting), recall@K saturation under millions
/// of chunks. Production validation with real data is the follow-up issue.
/// </summary>
public class InMemoryHybridDocumentVectorStore : IDocumentVectorStore
{
    private const int HybridRecallMultiplier = 2;
    private static readonly Regex TokenRegex = new(@"[\w\-]+", RegexOptions.Compiled);

    private readonly Dictionary<string, IndexedChunk> _chunksById = new();

    public VectorStoreCapabilities Capabilities { get; } = new()
    {
        SupportsVectorSearch = true,
        SupportsKeywordSearch = true,
        SupportsHybridSearch = true,
        SupportsStructuredFilter = true,
        SupportsDeleteByDocumentId = true,
        NormalizesScore = true
    };

    /// <summary>Seed the store from a benchmark dataset. Each chunk is indexed
    /// with its bigram set (for dense scoring) and token set (for sparse).</summary>
    public void Seed(IEnumerable<BenchmarkChunk> chunks)
    {
        foreach (var c in chunks)
        {
            _chunksById[c.Id] = new IndexedChunk
            {
                Id = c.Id,
                Text = c.Text,
                Bigrams = ComputeBigrams(c.Text),
                TokenSet = Tokenize(c.Text).ToHashSet()
            };
        }
    }

    public Task UpsertAsync(IReadOnlyList<DocumentVectorRecord> records, CancellationToken ct = default)
        => Task.CompletedTask; // Benchmark seeds via Seed(); IDocumentVectorStore.UpsertAsync is unused here.

    public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        var queryText = request.QueryText ?? string.Empty;

        IList<VectorSearchResult> results = request.Mode switch
        {
            VectorSearchMode.Vector => SearchVector(queryText, request.TopK),
            VectorSearchMode.Keyword => SearchKeyword(queryText, request.TopK),
            VectorSearchMode.Hybrid => SearchHybrid(queryText, request.TopK),
            _ => Array.Empty<VectorSearchResult>()
        };

        return Task.FromResult((IReadOnlyList<VectorSearchResult>)results.ToList());
    }

    private List<VectorSearchResult> SearchVector(string queryText, int topK)
    {
        var queryBigrams = ComputeBigrams(queryText);
        return _chunksById.Values
            .Select(c => new
            {
                Chunk = c,
                Score = CosineBigram(queryBigrams, c.Bigrams)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => ToResult(x.Chunk, x.Score))
            .ToList();
    }

    private List<VectorSearchResult> SearchKeyword(string queryText, int topK)
    {
        var queryTokens = Tokenize(queryText);
        if (queryTokens.Count == 0) return [];

        return _chunksById.Values
            .Select(c => new
            {
                Chunk = c,
                Score = TokenOverlap(queryTokens, c.TokenSet)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => ToResult(x.Chunk, x.Score))
            .ToList();
    }

    private List<VectorSearchResult> SearchHybrid(string queryText, int topK)
    {
        var recallTopK = topK * HybridRecallMultiplier;
        var vectorResults = SearchVector(queryText, recallTopK);
        var keywordResults = SearchKeyword(queryText, recallTopK);
        return RrfFusion.Merge(vectorResults, keywordResults, topK).ToList();
    }

    private static VectorSearchResult ToResult(IndexedChunk chunk, double score) => new()
    {
        // RecordId is a stable derived GUID so RrfFusion can dedupe by id —
        // critical because the same chunk may appear in both ranked lists.
        RecordId = DeterministicGuid(chunk.Id),
        DocumentId = DeterministicGuid(chunk.Id),
        ChunkIndex = 0,
        Text = chunk.Text,
        Score = score,
        DocumentTypeCode = chunk.Id  // Repurposed as the synthetic chunk id for assertion-time lookup.
    };

    // ── Scoring helpers ─────────────────────────────────────────────────

    private static double CosineBigram(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Intersect(b).Count();
        return inter / Math.Sqrt(a.Count * (double)b.Count);
    }

    private static double TokenOverlap(IReadOnlyList<string> queryTokens, ISet<string> chunkTokens)
    {
        if (queryTokens.Count == 0) return 0;
        var matched = queryTokens.Count(chunkTokens.Contains);
        return (double)matched / queryTokens.Count;
    }

    private static HashSet<string> ComputeBigrams(string s)
    {
        var bigrams = new HashSet<string>(StringComparer.Ordinal);
        var clean = s.ToLowerInvariant();
        for (var i = 0; i < clean.Length - 1; i++)
            bigrams.Add(clean.Substring(i, 2));
        return bigrams;
    }

    private static List<string> Tokenize(string s)
        => TokenRegex.Matches(s).Select(m => m.Value.ToLowerInvariant()).ToList();

    /// <summary>Stable GUID derived from chunk id, so the same chunk produces
    /// the same RecordId across vector and keyword paths (RrfFusion dedupes by id).</summary>
    private static Guid DeterministicGuid(string id)
    {
        var bytes = new byte[16];
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private sealed class IndexedChunk
    {
        public string Id { get; init; } = default!;
        public string Text { get; init; } = default!;
        public HashSet<string> Bigrams { get; init; } = default!;
        public HashSet<string> TokenSet { get; init; } = default!;
    }
}
