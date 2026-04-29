using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction.Digital;

[ExposeServices(typeof(IDigitalTextExtractor))]
public class PlainTextExtractor : IDigitalTextExtractor, ITransientDependency
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
