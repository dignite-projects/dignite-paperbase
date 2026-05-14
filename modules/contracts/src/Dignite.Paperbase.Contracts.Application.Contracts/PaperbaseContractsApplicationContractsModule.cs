using Volo.Abp.Application;
using Volo.Abp.Modularity;
using Volo.Abp.Authorization;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
    )]
public class PaperbaseContractsApplicationContractsModule : AbpModule
{

}
