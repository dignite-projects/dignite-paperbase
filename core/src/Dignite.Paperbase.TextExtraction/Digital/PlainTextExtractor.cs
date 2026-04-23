using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Dignite.Paperbase.TextExtraction.Digital;

internal class PlainTextExtractor : IDigitalTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".csv", ".rtf" };

    public bool CanHandle(string contentType, string fileExtension)
        => SupportedExtensions.Contains(fileExtension)
        || (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false);

    public async Task<string> ExtractAsync(Stream fileStream, string contentType)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }
}
