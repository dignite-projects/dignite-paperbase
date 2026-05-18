using System.ComponentModel.DataAnnotations;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class UpdateTenantFieldDefinitionDto
{
    [Required]
    [DynamicStringLength(typeof(TenantFieldConsts), nameof(TenantFieldConsts.MaxPromptLength))]
    public string Prompt { get; set; } = default!;

    public FieldDataType DataType { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; }
}
