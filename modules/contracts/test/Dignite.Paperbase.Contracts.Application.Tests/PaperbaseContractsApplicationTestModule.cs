using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsApplicationModule),
    typeof(PaperbaseContractsDomainTestModule)
    )]
public class PaperbaseContractsApplicationTestModule : AbpModule
{

}
