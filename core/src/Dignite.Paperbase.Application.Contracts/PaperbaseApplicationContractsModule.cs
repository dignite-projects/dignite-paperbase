using Volo.Abp.Application;
using Volo.Abp.Modularity;
using Volo.Abp.Authorization;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
    )]
public class PaperbaseApplicationContractsModule : AbpModule
{

}
