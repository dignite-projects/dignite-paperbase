using Volo.Abp.Application.Services;
using Dignite.Paperbase.Localization;

namespace Dignite.Paperbase.Host.Services;

/* Inherit your application services from this class. */
public abstract class PaperbaseHostAppService : ApplicationService
{
    protected PaperbaseHostAppService()
    {
        LocalizationResource = typeof(PaperbaseResource);
    }
}
