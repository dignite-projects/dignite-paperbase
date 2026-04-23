using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// 文档间关系。独立聚合根，不内嵌于 Document。
/// </summary>
public class DocumentRelation : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    /// <summary>来源文档 ID</summary>
    public virtual Guid SourceDocumentId { get; private set; }

    /// <summary>目标文档 ID</summary>
    public virtual Guid TargetDocumentId { get; private set; }

    /// <summary>关系类型，如 "related-to"、"supplements"、"supersedes"、"belongs-to"</summary>
    public virtual string RelationType { get; private set; } = default!;

    /// <summary>关系来源</summary>
    public virtual RelationSource Source { get; private set; }

    /// <summary>AI 推断的置信度（Manual 关系为 null）</summary>
    public virtual double? Confidence { get; private set; }

    protected DocumentRelation() { }

    public void Confirm() => Source = RelationSource.Manual;

    public DocumentRelation(
        Guid id,
        Guid? tenantId,
        Guid sourceDocumentId,
        Guid targetDocumentId,
        string relationType,
        RelationSource source,
        double? confidence = null)
        : base(id)
    {
        TenantId = tenantId;
        SourceDocumentId = sourceDocumentId;
        TargetDocumentId = targetDocumentId;
        RelationType = relationType;
        Source = source;
        Confidence = confidence;
    }
}
