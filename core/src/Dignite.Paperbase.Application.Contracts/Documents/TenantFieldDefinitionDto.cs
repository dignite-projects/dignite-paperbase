using System;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class TenantFieldDefinitionDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }
    public string DocumentTypeCode { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Prompt { get; set; } = default!;
    public FieldDataType DataType { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
}
