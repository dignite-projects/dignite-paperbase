using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Embedding;

public class AiEmbeddingIndexer : IEmbeddingIndexer, ITransientDependency
{
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public AiEmbeddingIndexer(
        TextChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
    }

    public virtual async Task<EmbeddingIndexResult> IndexAsync(
        EmbeddingIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var chunks = _chunker.Chunk(request.ExtractedText);
        var result = new EmbeddingIndexResult();

        for (var i = 0; i < chunks.Count; i++)
        {
            var embeddings = await _embeddingGenerator.GenerateAsync([chunks[i]], cancellationToken: cancellationToken);

            result.Chunks.Add(new EmbeddingChunkData
            {
                ChunkIndex = i,
                ChunkText = chunks[i],
                Vector = embeddings[0].Vector.ToArray()
            });
        }

        return result;
    }
}
