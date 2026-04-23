using System;
using System.IO;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;

namespace Dignite.Paperbase.TextExtraction.Digital;

internal class WordTextExtractor : IDigitalTextExtractor
{
    public bool CanHandle(string contentType, string fileExtension)
        => string.Equals(fileExtension, ".docx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileExtension, ".doc", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(Stream fileStream, string contentType)
    {
        using var wordDocument = WordprocessingDocument.Open(fileStream, false);
        var body = wordDocument.MainDocumentPart?.Document?.Body;
        return Task.FromResult(body?.InnerText ?? string.Empty);
    }
}
