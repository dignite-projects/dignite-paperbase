using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Paperbase.Contracts;

public abstract class ContractsController : AbpControllerBase
{
    protected ContractsController()
    {
        LocalizationResource = typeof(ContractsResource);
    }
}
