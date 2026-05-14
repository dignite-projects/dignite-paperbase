using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Contracts;

public abstract class PaperbaseContractsAppService : ApplicationService
{
    protected PaperbaseContractsAppService()
    {
        LocalizationResource = typeof(PaperbaseContractsResource);
        ObjectMapperContext = typeof(PaperbaseContractsApplicationModule);
    }
}
