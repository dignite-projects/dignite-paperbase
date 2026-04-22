using Localization.Resources.AbpUi;
using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(ContractsApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule))]
public class ContractsHttpApiModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<IMvcBuilder>(mvcBuilder =>
        {
            mvcBuilder.AddApplicationPartIfNotExists(typeof(ContractsHttpApiModule).Assembly);
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<ContractsResource>()
                .AddBaseTypes(typeof(AbpUiResource));
        });
    }
}
