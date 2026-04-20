using Microsoft.Extensions.Localization;
using Dignite.Paperbase.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Dignite.Paperbase;

[Dependency(ReplaceServices = true)]
public class PaperbaseBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<PaperbaseResource> _localizer;

    public PaperbaseBrandingProvider(IStringLocalizer<PaperbaseResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}