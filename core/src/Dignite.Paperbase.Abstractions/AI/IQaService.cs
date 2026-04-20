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
    public int? PageNumber { get; set; }
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
