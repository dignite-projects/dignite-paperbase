using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class DocumentTenantFieldDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public string FieldName { get; set; } = default!;
    public string? Value { get; set; }
    public double? Confidence { get; set; }
}
