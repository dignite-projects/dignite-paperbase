using System.ComponentModel.DataAnnotations;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class CreateTenantFieldDefinitionDto
{
    [Required]
    [DynamicStringLength(typeof(TenantFieldConsts), nameof(TenantFieldConsts.MaxDocumentTypeCodeLength))]
    public string DocumentTypeCode { get; set; } = default!;

    [Required]
    [DynamicStringLength(typeof(TenantFieldConsts), nameof(TenantFieldConsts.MaxNameLength))]
    public string Name { get; set; } = default!;

    [Required]
    [DynamicStringLength(typeof(TenantFieldConsts), nameof(TenantFieldConsts.MaxPromptLength))]
    public string Prompt { get; set; } = default!;

    public FieldDataType DataType { get; set; } = FieldDataType.String;

    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; }
}
