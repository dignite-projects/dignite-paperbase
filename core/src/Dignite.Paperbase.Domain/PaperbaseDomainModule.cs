using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Volo.Abp.Commercial.SuiteTemplates;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VoloAbpCommercialSuiteTemplatesModule),
    typeof(PaperbaseDomainSharedModule)
)]
public class PaperbaseDomainModule : AbpModule
{

}
