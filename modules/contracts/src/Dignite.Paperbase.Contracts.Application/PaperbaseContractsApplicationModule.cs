using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.Mapperly;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsDomainModule),
    typeof(PaperbaseContractsApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpMapperlyModule)
    )]
public class PaperbaseContractsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<PaperbaseContractsApplicationModule>();
    }
}
