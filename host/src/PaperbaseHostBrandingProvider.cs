using Microsoft.Extensions.Localization;
using Dignite.Paperbase.Host.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Dignite.Paperbase.Host;

[Dependency(ReplaceServices = true)]
public class PaperbaseHostBrandingProvider : DefaultBrandingProvider
{
    private readonly IStringLocalizer<PaperbaseHostResource> _localizer;

    public PaperbaseHostBrandingProvider(IStringLocalizer<PaperbaseHostResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
