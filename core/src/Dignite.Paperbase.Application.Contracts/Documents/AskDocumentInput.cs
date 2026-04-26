using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class AskDocumentInput
{
    [Required]
    [DynamicStringLength(typeof(DocumentQaConsts), nameof(DocumentQaConsts.MaxQuestionLength))]
    public string Question { get; set; } = default!;

    public QaMode Mode { get; set; } = QaMode.Auto;
}
