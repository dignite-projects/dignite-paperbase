using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dignite.Paperbase.Chat;

public class CreateChatConversationInput : IValidatableObject
{
    [StringLength(200)]
    public string? Title { get; set; }

    public Guid? DocumentId { get; set; }

    [StringLength(64)]
    public string? DocumentTypeCode { get; set; }

    [Range(1, 100)]
    public int? TopK { get; set; }

    [Range(0.0, 1.0)]
    public double? MinScore { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DocumentId.HasValue && !string.IsNullOrWhiteSpace(DocumentTypeCode))
        {
            yield return new ValidationResult(
                "DocumentId and DocumentTypeCode are mutually exclusive.",
                new[] { nameof(DocumentId), nameof(DocumentTypeCode) });
        }
    }
}
