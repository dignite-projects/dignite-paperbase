using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class GlobalAskInput
{
    [Required]
    [DynamicStringLength(typeof(DocumentQaConsts), nameof(DocumentQaConsts.MaxQuestionLength))]
    public string Question { get; set; } = default!;

    [DynamicStringLength(typeof(DocumentConsts), nameof(DocumentConsts.MaxDocumentTypeCodeLength))]
    public string? DocumentTypeCode { get; set; }
}
