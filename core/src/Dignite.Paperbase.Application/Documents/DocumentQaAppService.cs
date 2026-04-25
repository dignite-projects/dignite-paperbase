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
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly PaperbaseAIOptions _aiOptions;

    public DocumentQaAppService(
        IDocumentChunkRepository chunkRepository,
        DocumentQaWorkflow qaWorkflow,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<PaperbaseAIOptions> aiOptions)
    {
        _chunkRepository = chunkRepository;
        _qaWorkflow = qaWorkflow;
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

        var qaChunks = chunks.Select(c => new QaChunk
        {
            ChunkIndex = c.ChunkIndex,
            ChunkText = c.ChunkText
        }).ToList();

        var outcome = await _qaWorkflow.RunRagAsync(input.Question, qaChunks);

        return new QaResultDto
        {
            Answer = outcome.Answer,
            ActualMode = outcome.ActualMode.ToString(),
            IsDegraded = false,
            Sources = outcome.Sources.Select(s => new QaSourceDto
            {
                Text = s.Text,
                ChunkIndex = s.ChunkIndex
            }).ToList()
        };
    }
}
