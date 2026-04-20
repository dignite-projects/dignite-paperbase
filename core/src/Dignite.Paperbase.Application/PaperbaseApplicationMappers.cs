using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace Dignite.Paperbase;

/// <summary>
/// Document -> DocumentDto
/// FileOrigin and PipelineRun nested mappings are consolidated here (Mapperly compile-time constraint).
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class DocumentToDocumentDtoMapper : MapperBase<Document, DocumentDto>
{
    public override partial DocumentDto Map(Document source);
    public override partial void Map(Document source, DocumentDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class DocumentRelationToDocumentRelationDtoMapper : MapperBase<DocumentRelation, DocumentRelationDto>
{
    public override partial DocumentRelationDto Map(DocumentRelation source);
    public override partial void Map(DocumentRelation source, DocumentRelationDto destination);
}
