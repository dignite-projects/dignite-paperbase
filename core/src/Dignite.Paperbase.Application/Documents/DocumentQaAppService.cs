using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Dignite.Paperbase.Application.Documents;

[Authorize(PaperbasePermissions.Documents.Ask)]
public class DocumentQaAppService : PaperbaseAppService, IDocumentQaAppService
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IQaService _qaService;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentQaAppService(
        IDocumentChunkRepository chunkRepository,
        IQaService qaService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _chunkRepository = chunkRepository;
        _qaService = qaService;
        _embeddingGenerator = embeddingGenerator;
        _aiOptions = aiOptions.Value;
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
                ActualMode = QaMode.Rag.ToString()
            };
        }

        var request = new QaRequest
        {
            DocumentId = System.Guid.Empty,
            Question = input.Question,
            Mode = QaMode.Rag,
            HasEmbedding = true,
            Chunks = chunks.Select(c => new QaChunkData
            {
                ChunkIndex = c.ChunkIndex,
                ChunkText = c.ChunkText
            }).ToList<QaChunkData>()
        };

        var result = await _qaService.AskAsync(request);

        return new QaResultDto
        {
            Answer = result.Answer,
            ActualMode = result.ActualMode.ToString(),
            IsDegraded = false,
            Sources = result.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }
}
