using Microsoft.Extensions.Localization;
using Dignite.Paperbase.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Dignite.Paperbase.Host;

[Dependency(ReplaceServices = true)]
public class PaperbaseHostBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<PaperbaseResource> _localizer;

    public PaperbaseHostBrandingProvider(IStringLocalizer<PaperbaseResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}