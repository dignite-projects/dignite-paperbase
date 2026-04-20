using Volo.Abp.Application.Services;
using Dignite.Paperbase.Localization;

namespace Dignite.Paperbase.Services;

/* Inherit your application services from this class. */
public abstract class PaperbaseAppService : ApplicationService
{
    protected PaperbaseAppService()
    {
        LocalizationResource = typeof(PaperbaseResource);
    }
}