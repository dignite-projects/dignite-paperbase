using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Permissions;
using Dignite.Paperbase.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dignite.Paperbase.Application.Documents;

[Authorize(PaperbasePermissions.Documents.Ask)]
public class DocumentQaAppService : PaperbaseAppService, IDocumentQaAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly DocumentRerankWorkflow _rerankWorkflow;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;
    private readonly PaperbaseRagOptions _ragOptions;

    public DocumentQaAppService(
        IDocumentRepository documentRepository,
        IDocumentKnowledgeIndex vectorStore,
        DocumentQaWorkflow qaWorkflow,
        DocumentRerankWorkflow rerankWorkflow,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions,
        IOptions<PaperbaseRagOptions> ragOptions)
    {
        _documentRepository = documentRepository;
        _vectorStore = vectorStore;
        _qaWorkflow = qaWorkflow;
        _rerankWorkflow = rerankWorkflow;
        _embeddingGenerator = embeddingGenerator;
        _aiOptions = aiOptions.Value;
        _ragOptions = ragOptions.Value;
    }

    public virtual async Task<QaResultDto> AskAsync(Guid documentId, AskDocumentInput input)
    {
        var document = await _documentRepository.GetAsync(documentId);

        DocumentQaOutcome outcome;
        if (document.HasEmbedding)
        {
            var qaChunks = await RetrieveQaChunksAsync(
                input.Question,
                finalTopK: _aiOptions.QaTopKChunks,
                baselineMultiplier: 1,
                documentId: documentId);

            if (qaChunks.Count == 0)
            {
                return new QaResultDto
                {
                    Answer = L["Document:NoRelevantContextFound"],
                    ActualMode = QaMode.Rag,
                    IsDegraded = false
                };
            }

            outcome = await _qaWorkflow.RunRagAsync(input.Question, qaChunks);
        }
        else
        {
            outcome = await _qaWorkflow.RunFullTextAsync(input.Question, document.ExtractedText);
        }

        return new QaResultDto
        {
            Answer = outcome.Answer,
            ActualMode = outcome.ActualMode,
            IsDegraded = !document.HasEmbedding,
            Sources = outcome.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }

    public virtual async Task<QaResultDto> GlobalAskAsync(GlobalAskInput input)
    {
        var qaChunks = await RetrieveQaChunksAsync(
            input.Question,
            finalTopK: _aiOptions.QaTopKChunks,
            baselineMultiplier: 3,
            documentTypeCode: input.DocumentTypeCode);

        if (qaChunks.Count == 0)
        {
            return new QaResultDto
            {
                Answer = L["Document:NoRelevantDocumentsFound"],
                ActualMode = QaMode.Rag
            };
        }

        var outcome = await _qaWorkflow.RunRagAsync(input.Question, qaChunks);

        return new QaResultDto
        {
            Answer = outcome.Answer,
            ActualMode = outcome.ActualMode,
            IsDegraded = false,
            Sources = outcome.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }

    /// <summary>
    /// 统一的检索 + 阈值过滤 + 可选 LLM 精排管线。
    /// <paramref name="finalTopK"/> 是最终供 RAG 使用的 chunk 数；
    /// <paramref name="baselineMultiplier"/> 是关闭精排时的召回倍数（GlobalAsk 历史上是 3，单文档是 1）。
    /// 启用 <see cref="PaperbaseAIOptions.EnableLlmRerank"/> 时召回 = finalTopK × RecallExpandFactor，
    /// 然后用 LLM 重排取前 finalTopK。
    /// </summary>
    protected virtual async Task<List<QaChunk>> RetrieveQaChunksAsync(
        string question,
        int finalTopK,
        int baselineMultiplier,
        Guid? documentId = null,
        string? documentTypeCode = null)
    {
        var rerank = _aiOptions.EnableLlmRerank;
        var recallTopK = rerank
            ? finalTopK * Math.Max(1, _aiOptions.RecallExpandFactor)
            : finalTopK * Math.Max(1, baselineMultiplier);

        var questionEmbeddings = await _embeddingGenerator.GenerateAsync([question]);
        var capabilities = _vectorStore.Capabilities;
        EnsureSearchCapabilities(capabilities);
        var searchMode = ResolveSearchMode(_ragOptions.DefaultSearchMode, capabilities);
        var canApplyMinScore = capabilities.NormalizesScore && _aiOptions.QaMinScore > 0;

        // QueryText is always passed so providers running in Hybrid / Keyword mode
        // have a textual query available; Vector-only providers ignore it.
        // Mode comes from PaperbaseRagOptions.DefaultSearchMode (Vector by default)
        // so flipping to Hybrid is a config change, not a code change.
        var request = new VectorSearchRequest
        {
            QueryVector = questionEmbeddings[0].Vector,
            QueryText = question,
            TopK = recallTopK,
            DocumentId = documentId,
            DocumentTypeCode = documentTypeCode,
            MinScore = canApplyMinScore ? _aiOptions.QaMinScore : null,
            Mode = searchMode
        };

        var rawResults = await _vectorStore.SearchForCurrentTenantAsync(CurrentTenant, request);
        var filtered = canApplyMinScore ? ApplyMinScoreFilter(rawResults) : rawResults;
        if (filtered.Count == 0)
            return new List<QaChunk>();

        if (rerank && filtered.Count > finalTopK)
        {
            var candidates = filtered
                .Select(r => new RerankCandidate(r.Text, r.Score ?? 0.0, r))
                .ToList();

            var reranked = await _rerankWorkflow.RerankAsync(question, candidates, finalTopK);

            return reranked
                .Select(r =>
                {
                    var result = (VectorSearchResult)r.Candidate.Tag!;
                    return new QaChunk { ChunkIndex = result.ChunkIndex, ChunkText = result.Text };
                })
                .ToList();
        }

        return filtered
            .Take(finalTopK)
            .Select(r => new QaChunk { ChunkIndex = r.ChunkIndex, ChunkText = r.Text })
            .ToList();
    }

    protected virtual void EnsureSearchCapabilities(DocumentKnowledgeIndexCapabilities capabilities)
    {
        if (!capabilities.SupportsStructuredFilter)
        {
            throw new InvalidOperationException(
                "The configured document vector store does not support structured filters. " +
                "Paperbase requires tenant/document/type filters to avoid leaking search results across scopes.");
        }
    }

    protected virtual VectorSearchMode ResolveSearchMode(
        VectorSearchMode requestedMode,
        DocumentKnowledgeIndexCapabilities capabilities)
    {
        return requestedMode switch
        {
            VectorSearchMode.Vector when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            VectorSearchMode.Keyword when capabilities.SupportsKeywordSearch => VectorSearchMode.Keyword,
            VectorSearchMode.Hybrid when capabilities.SupportsHybridSearch => VectorSearchMode.Hybrid,
            VectorSearchMode.Hybrid when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            VectorSearchMode.Keyword when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            _ => throw new InvalidOperationException(
                $"The configured document vector store does not support requested search mode '{requestedMode}' " +
                "and cannot fall back to vector search.")
        };
    }

    /// <summary>
    /// 按 <see cref="PaperbaseAIOptions.QaMinScore"/> 过滤掉相似度过低的检索结果。
    /// 当全部命中均低于阈值时记录信息日志，便于事后调优。
    /// </summary>
    protected virtual IReadOnlyList<VectorSearchResult> ApplyMinScoreFilter(
        IReadOnlyList<VectorSearchResult> rawResults)
    {
        if (_aiOptions.QaMinScore <= 0 || rawResults.Count == 0)
        {
            return rawResults;
        }

        var filtered = rawResults
            .Where(r => r.Score >= _aiOptions.QaMinScore)
            .ToList();

        if (filtered.Count == 0)
        {
            Logger.LogInformation(
                "All {Count} retrieved chunks below QaMinScore={MinScore}; top score={TopScore:F3}",
                rawResults.Count,
                _aiOptions.QaMinScore,
                rawResults[0].Score);
        }

        return filtered;
    }
}
