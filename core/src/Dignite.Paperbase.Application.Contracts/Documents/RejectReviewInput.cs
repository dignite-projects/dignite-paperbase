using System.ComponentModel.DataAnnotations;

namespace Dignite.Paperbase.Documents;

public class RejectReviewInput
{
    [StringLength(512)]
    public string? Reason { get; set; }
}
