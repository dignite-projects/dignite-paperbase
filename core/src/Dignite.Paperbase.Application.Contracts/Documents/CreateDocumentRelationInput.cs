using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class CreateDocumentRelationInput
{
    [Required]
    public Guid SourceDocumentId { get; set; }

    [Required]
    public Guid TargetDocumentId { get; set; }

    [Required]
    [DynamicStringLength(typeof(DocumentRelationConsts), nameof(DocumentRelationConsts.MaxDescriptionLength))]
    public string Description { get; set; } = default!;
}
