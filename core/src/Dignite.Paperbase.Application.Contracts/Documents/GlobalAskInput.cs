using System.ComponentModel.DataAnnotations;

namespace Dignite.Paperbase.Documents;

public class GlobalAskInput
{
    [Required]
    [StringLength(1000)]
    public string Question { get; set; } = default!;

    public string? DocumentTypeCode { get; set; }
}
