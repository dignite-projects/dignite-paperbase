using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// AI 关系推断能力端口（非关键流水线）。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IRelationInferrer
{
    Task<IList<InferredRelation>> InferAsync(
        RelationInferenceRequest request,
        CancellationToken cancellationToken = default);
}

public class RelationInferenceRequest
{
    public Guid DocumentId { get; set; }
    public string ExtractedText { get; set; } = default!;
    public string? DocumentTypeCode { get; set; }
    public IList<DocumentSummary> Candidates { get; set; } = new List<DocumentSummary>();
}

public class DocumentSummary
{
    public Guid DocumentId { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string Summary { get; set; } = default!;
}

public class InferredRelation
{
    public Guid TargetDocumentId { get; set; }
    public string RelationType { get; set; } = default!;
    public double Confidence { get; set; }
}
