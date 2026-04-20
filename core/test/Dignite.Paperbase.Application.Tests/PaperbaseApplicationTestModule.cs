using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseApplicationModule),
    typeof(PaperbaseDomainTestModule)
    )]
public class PaperbaseApplicationTestModule : AbpModule
{

}
