using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(PaperbaseDomainSharedModule)
)]
public class PaperbaseDomainModule : AbpModule
{

}
