using System.ComponentModel.DataAnnotations;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.Documents;

public class AskDocumentInput
{
    [Required]
    [StringLength(1000)]
    public string Question { get; set; } = default!;

    public QaMode Mode { get; set; } = QaMode.Auto;
}
