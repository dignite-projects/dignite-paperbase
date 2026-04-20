using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 文档向量化能力端口（非关键流水线）。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IEmbeddingIndexer
{
    Task IndexAsync(
        EmbeddingIndexRequest request,
        CancellationToken cancellationToken = default);
}

public class EmbeddingIndexRequest
{
    public Guid DocumentId { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string ExtractedText { get; set; } = default!;
}
