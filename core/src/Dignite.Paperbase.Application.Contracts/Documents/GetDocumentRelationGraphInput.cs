using System;
using System.ComponentModel.DataAnnotations;

namespace Dignite.Paperbase.Documents;

public class GetDocumentRelationGraphInput
{
    [Required]
    public Guid RootDocumentId { get; set; }

    [Range(1, 3)]
    public int Depth { get; set; } = 1;

    public bool IncludeAiSuggested { get; set; } = true;
}
