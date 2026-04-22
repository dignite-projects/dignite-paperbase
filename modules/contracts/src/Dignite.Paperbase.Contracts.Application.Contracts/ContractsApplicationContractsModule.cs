using Volo.Abp.Application;
using Volo.Abp.Modularity;
using Volo.Abp.Authorization;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(ContractsDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
    )]
public class ContractsApplicationContractsModule : AbpModule
{

}
