using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Contracts;

public abstract class ContractsAppService : ApplicationService
{
    protected ContractsAppService()
    {
        LocalizationResource = typeof(ContractsResource);
        ObjectMapperContext = typeof(ContractsApplicationModule);
    }
}
