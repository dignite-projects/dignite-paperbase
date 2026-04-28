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
    private readonly IDocumentVectorStore _vectorStore;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly DocumentRerankWorkflow _rerankWorkflow;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentQaAppService(
        IDocumentRepository documentRepository,
        IDocumentVectorStore vectorStore,
        DocumentQaWorkflow qaWorkflow,
        DocumentRerankWorkflow rerankWorkflow,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _vectorStore = vectorStore;
        _qaWorkflow = qaWorkflow;
        _rerankWorkflow = rerankWorkflow;
        _embeddingGenerator = embeddingGenerator;
        _aiOptions = aiOptions.Value;
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
        var request = new VectorSearchRequest
        {
            QueryVector = questionEmbeddings[0].Vector,
            TopK = recallTopK,
            DocumentId = documentId,
            DocumentTypeCode = documentTypeCode,
            MinScore = _aiOptions.QaMinScore > 0 ? _aiOptions.QaMinScore : null,
            Mode = VectorSearchMode.Vector
        };

        var rawResults = await _vectorStore.SearchForCurrentTenantAsync(CurrentTenant, request);
        var filtered = ApplyMinScoreFilter(rawResults);
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
