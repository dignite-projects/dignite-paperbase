using System;
using System.ComponentModel.DataAnnotations;

namespace Dignite.Paperbase.Documents;

public class CreateDocumentRelationInput
{
    [Required]
    public Guid SourceDocumentId { get; set; }

    [Required]
    public Guid TargetDocumentId { get; set; }

    [Required]
    [StringLength(DocumentRelationConsts.MaxDescriptionLength)]
    public string Description { get; set; } = default!;
}
