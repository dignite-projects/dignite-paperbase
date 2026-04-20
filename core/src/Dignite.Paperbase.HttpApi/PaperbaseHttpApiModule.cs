using Localization.Resources.AbpUi;
using Dignite.Paperbase.Localization;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule))]
public class PaperbaseHttpApiModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<IMvcBuilder>(mvcBuilder =>
        {
            mvcBuilder.AddApplicationPartIfNotExists(typeof(PaperbaseHttpApiModule).Assembly);
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<PaperbaseResource>()
                .AddBaseTypes(typeof(AbpUiResource));
        });
    }
}
