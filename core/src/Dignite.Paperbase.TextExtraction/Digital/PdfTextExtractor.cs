using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dignite.Paperbase.TextExtraction.Digital;

internal class PdfTextExtractor : IDigitalTextExtractor
{
    public bool CanHandle(string contentType, string fileExtension)
        => string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(Stream fileStream, string contentType)
    {
        try
        {
            using var document = PdfDocument.Open(fileStream);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var words = string.Join(" ", page.GetWords().Select((Word w) => w.Text));
                if (!string.IsNullOrWhiteSpace(words))
                    sb.AppendLine(words);
            }
            var text = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new NoTextLayerException();

            return Task.FromResult(text);
        }
        catch (NoTextLayerException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new NoTextLayerException();
        }
    }
}
