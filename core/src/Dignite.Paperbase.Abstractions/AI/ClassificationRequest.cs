using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.AI;

public class ClassificationRequest
{
    public string ExtractedText { get; set; } = default!;
    public IList<DocumentTypeHint> CandidateTypes { get; set; } = new List<DocumentTypeHint>();
}

public class DocumentTypeHint
{
    public string TypeCode { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public IList<string> Keywords { get; set; } = new List<string>();
}
