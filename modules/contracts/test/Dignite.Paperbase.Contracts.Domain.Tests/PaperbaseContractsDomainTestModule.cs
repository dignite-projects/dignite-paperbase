using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsDomainModule),
    typeof(PaperbaseContractsTestBaseModule)
)]
public class PaperbaseContractsDomainTestModule : AbpModule
{

}
