using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Paperbase.Contracts;

public abstract class PaperbaseContractsController : AbpControllerBase
{
    protected PaperbaseContractsController()
    {
        LocalizationResource = typeof(PaperbaseContractsResource);
    }
}
