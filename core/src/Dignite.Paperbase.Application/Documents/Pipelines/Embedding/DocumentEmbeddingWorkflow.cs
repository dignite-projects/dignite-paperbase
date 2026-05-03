using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.Embedding;

/// <summary>
/// 文档向量化 Workflow：分块 → 批量调用 IEmbeddingGenerator 生成向量。
/// </summary>
public class DocumentEmbeddingWorkflow : ITransientDependency
{
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public DocumentEmbeddingWorkflow(
        TextChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
    }

    public virtual async Task<IReadOnlyList<DocumentEmbeddingChunk>> RunAsync(
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var chunks = _chunker.Chunk(markdown);
        if (chunks.Count == 0)
            return [];

        var allEmbeddings = await _embeddingGenerator.GenerateAsync(
            chunks, cancellationToken: cancellationToken);

        var results = new List<DocumentEmbeddingChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            results.Add(new DocumentEmbeddingChunk
            {
                ChunkIndex = i,
                ChunkText = chunks[i],
                Vector = allEmbeddings[i].Vector.ToArray()
            });
        }

        return results;
    }
}

public class DocumentEmbeddingChunk
{
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = default!;
    public float[] Vector { get; set; } = default!;
}
