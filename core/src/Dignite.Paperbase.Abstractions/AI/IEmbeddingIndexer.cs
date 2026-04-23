using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 文档向量化能力端口（非关键流水线）。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IEmbeddingIndexer
{
    Task<EmbeddingIndexResult> IndexAsync(
        EmbeddingIndexRequest request,
        CancellationToken cancellationToken = default);
}

public class EmbeddingIndexRequest
{
    public Guid DocumentId { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string ExtractedText { get; set; } = default!;
}

public class EmbeddingIndexResult
{
    public IList<EmbeddingChunkData> Chunks { get; set; } = new List<EmbeddingChunkData>();
}

public class EmbeddingChunkData
{
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = default!;
    public float[] Vector { get; set; } = default!;
}
