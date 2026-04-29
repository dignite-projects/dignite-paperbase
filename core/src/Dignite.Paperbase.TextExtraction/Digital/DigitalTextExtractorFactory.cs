using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction.Digital;

public class DigitalTextExtractorFactory : IDigitalTextExtractorFactory, ITransientDependency
{
    private readonly IEnumerable<IDigitalTextExtractor> _extractors;

    public DigitalTextExtractorFactory(IEnumerable<IDigitalTextExtractor> extractors)
    {
        _extractors = extractors;
    }

    public IDigitalTextExtractor GetExtractor(string contentType, string fileExtension)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(contentType, fileExtension));
        if (extractor == null)
            throw new BusinessException("Paperbase:UnsupportedDigitalFileFormat")
                .WithData("ContentType", contentType)
                .WithData("Extension", fileExtension);

        return extractor;
    }
}
