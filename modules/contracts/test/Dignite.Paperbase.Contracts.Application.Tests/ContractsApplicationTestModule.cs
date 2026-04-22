using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(ContractsApplicationModule),
    typeof(ContractsDomainTestModule)
    )]
public class ContractsApplicationTestModule : AbpModule
{

}
