using Dignite.Paperbase.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Paperbase;

public abstract class PaperbaseController : AbpControllerBase
{
    protected PaperbaseController()
    {
        LocalizationResource = typeof(PaperbaseResource);
    }
}
