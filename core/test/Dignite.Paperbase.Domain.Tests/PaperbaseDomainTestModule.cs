using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseDomainModule),
    typeof(PaperbaseTestBaseModule)
)]
public class PaperbaseDomainTestModule : AbpModule
{

}
