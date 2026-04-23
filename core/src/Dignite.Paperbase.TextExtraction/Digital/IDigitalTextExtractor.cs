using System.IO;
using System.Threading.Tasks;

namespace Dignite.Paperbase.TextExtraction.Digital;

public interface IDigitalTextExtractor
{
    bool CanHandle(string contentType, string fileExtension);
    Task<string> ExtractAsync(Stream fileStream, string contentType);
}
