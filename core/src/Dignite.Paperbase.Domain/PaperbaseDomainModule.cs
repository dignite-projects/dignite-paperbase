using Dignite.Paperbase.Abstractions;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(AbpBlobStoringModule),
    typeof(PaperbaseAbstractionsModule),
    typeof(PaperbaseDomainSharedModule)
)]
public class PaperbaseDomainModule : AbpModule
{
}
