using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class ConfirmClassificationInput
{
    [Required]
    [DynamicStringLength(typeof(DocumentConsts), nameof(DocumentConsts.MaxDocumentTypeCodeLength))]
    public string DocumentTypeCode { get; set; } = default!;
}
