using Dignite.Paperbase.Localization;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase;

public abstract class PaperbaseAppService : ApplicationService
{
    protected PaperbaseAppService()
    {
        LocalizationResource = typeof(PaperbaseResource);
        ObjectMapperContext = typeof(PaperbaseApplicationModule);
    }
}
