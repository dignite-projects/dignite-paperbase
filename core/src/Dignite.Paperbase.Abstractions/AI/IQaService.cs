using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 文档问答能力端口。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IQaService
{
    Task<QaResult> AskAsync(
        QaRequest request,
        CancellationToken cancellationToken = default);
}

public class QaRequest
{
    public Guid DocumentId { get; set; }
    public string Question { get; set; } = default!;
    public QaMode Mode { get; set; } = QaMode.Auto;

    /// <summary>Application 层预填充：文档是否已完成向量化</summary>
    public bool HasEmbedding { get; set; }

    /// <summary>Application 层预填充：用于 FullText 模式的完整提取文本</summary>
    public string? ExtractedText { get; set; }

    /// <summary>Application 层预填充：向量检索后的 Top-K chunks，用于 RAG 模式</summary>
    public IList<QaChunkData> Chunks { get; set; } = new List<QaChunkData>();
}

public class QaChunkData
{
    public string ChunkText { get; set; } = default!;
    public int ChunkIndex { get; set; }
}

public class QaResult
{
    public string Answer { get; set; } = default!;
    public IList<QaSource> Sources { get; set; } = new List<QaSource>();
    public QaMode ActualMode { get; set; }
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

public class QaSource
{
    public string Text { get; set; } = default!;
    public int? ChunkIndex { get; set; }
}

public enum QaMode
{
    /// <summary>自动：有 embedding 用 RAG，否则降级全文</summary>
    Auto = 0,
    /// <summary>强制使用 RAG 向量检索</summary>
    Rag = 1,
    /// <summary>强制使用全文检索</summary>
    FullText = 2
}
