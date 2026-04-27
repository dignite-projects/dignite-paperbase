using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Documents;

public class CreateDocumentRelationInput
{
    public Guid SourceDocumentId { get; set; }

    public Guid TargetDocumentId { get; set; }

    [Required]
    [DynamicStringLength(typeof(DocumentRelationConsts), nameof(DocumentRelationConsts.MaxDescriptionLength))]
    public string Description { get; set; } = default!;
}
