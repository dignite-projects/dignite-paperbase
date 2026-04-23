namespace Dignite.Paperbase.TextExtraction.Digital;

public interface IDigitalTextExtractorFactory
{
    IDigitalTextExtractor GetExtractor(string contentType, string fileExtension);
}
