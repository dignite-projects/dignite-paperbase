using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(ContractsDomainModule),
    typeof(ContractsTestBaseModule)
)]
public class ContractsDomainTestModule : AbpModule
{

}
