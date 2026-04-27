using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Dignite.Paperbase.Application.Documents;

[Authorize(PaperbasePermissions.Documents.Ask)]
public class DocumentQaAppService : PaperbaseAppService, IDocumentQaAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentQaAppService(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        DocumentQaWorkflow qaWorkflow,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _qaWorkflow = qaWorkflow;
        _embeddingGenerator = embeddingGenerator;
        _aiOptions = aiOptions.Value;
    }

    public virtual async Task<QaResultDto> AskAsync(Guid documentId, AskDocumentInput input)
    {
        var document = await _documentRepository.GetAsync(documentId);
        var actualMode = DetermineActualMode(input.Mode, document.HasEmbedding);

        DocumentQaOutcome outcome;
        if (actualMode == QaMode.Rag)
        {
            var questionEmbeddings = await _embeddingGenerator.GenerateAsync([input.Question]);
            var chunks = await _chunkRepository.SearchByVectorAsync(
                questionEmbeddings[0].Vector.ToArray(), _aiOptions.QaTopKChunks, documentId: documentId);

            var qaChunks = chunks.Select(c => new QaChunk
            {
                ChunkIndex = c.ChunkIndex,
                ChunkText = c.ChunkText
            }).ToList();

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
            IsDegraded = input.Mode == QaMode.Auto && !document.HasEmbedding,
            Sources = outcome.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }

    private static QaMode DetermineActualMode(QaMode requested, bool hasEmbedding)
    {
        if (requested == QaMode.Rag) return QaMode.Rag;
        if (requested == QaMode.FullText) return QaMode.FullText;
        return hasEmbedding ? QaMode.Rag : QaMode.FullText;
    }

    public virtual async Task<QaResultDto> GlobalAskAsync(GlobalAskInput input)
    {
        var questionEmbeddings = await _embeddingGenerator.GenerateAsync([input.Question]);
        var chunks = await _chunkRepository.SearchByVectorAsync(
            questionEmbeddings[0].Vector.ToArray(),
            topK: _aiOptions.QaTopKChunks * 3,
            documentTypeCode: input.DocumentTypeCode);

        if (chunks.Count == 0)
        {
            return new QaResultDto
            {
                Answer = L["Document:NoRelevantDocumentsFound"],
                ActualMode = QaMode.Rag
            };
        }

        var qaChunks = chunks.Select(c => new QaChunk
        {
            ChunkIndex = c.ChunkIndex,
            ChunkText = c.ChunkText
        }).ToList();

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
}
