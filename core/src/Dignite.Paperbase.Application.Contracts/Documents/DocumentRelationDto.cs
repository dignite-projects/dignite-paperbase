using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class DocumentRelationDto : EntityDto<Guid>
{
    public Guid SourceDocumentId { get; set; }
    public Guid TargetDocumentId { get; set; }
    public string RelationType { get; set; } = default!;
    public RelationSource Source { get; set; }
    public double? Confidence { get; set; }
    public DateTime CreationTime { get; set; }
}
