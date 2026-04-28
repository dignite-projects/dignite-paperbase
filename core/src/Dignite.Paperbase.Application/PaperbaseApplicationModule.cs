using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Mapperly;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseAbstractionsModule),
    typeof(PaperbaseDomainModule),
    typeof(PaperbaseApplicationContractsModule),
    typeof(PaperbaseRagModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpBackgroundJobsModule),
    typeof(AbpMapperlyModule)
    )]
public class PaperbaseApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<PaperbaseApplicationModule>();
    }
}
