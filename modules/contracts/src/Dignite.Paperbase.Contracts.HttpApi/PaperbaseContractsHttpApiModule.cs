using Localization.Resources.AbpUi;
using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule))]
public class PaperbaseContractsHttpApiModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<IMvcBuilder>(mvcBuilder =>
        {
            mvcBuilder.AddApplicationPartIfNotExists(typeof(PaperbaseContractsHttpApiModule).Assembly);
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<PaperbaseContractsResource>()
                .AddBaseTypes(typeof(AbpUiResource));
        });
    }
}
