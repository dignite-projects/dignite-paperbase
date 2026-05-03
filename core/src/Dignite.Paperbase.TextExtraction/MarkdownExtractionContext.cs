using System.Collections.Generic;

namespace Dignite.Paperbase.TextExtraction;

public class MarkdownExtractionContext
{
    public string ContentType { get; set; } = default!;
    public string FileExtension { get; set; } = default!;
    public IList<string> LanguageHints { get; set; } = new List<string>();
}
