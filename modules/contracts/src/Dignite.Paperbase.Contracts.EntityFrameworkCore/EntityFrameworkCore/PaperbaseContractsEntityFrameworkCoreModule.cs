using Dignite.Paperbase.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[DependsOn(
    typeof(PaperbaseContractsDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class PaperbaseContractsEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PaperbaseContractsDbContext>(options =>
        {
            options.AddDefaultRepositories<IPaperbaseContractsDbContext>();
            options.AddRepository<Contract, EfCoreContractRepository>();
        });
    }
}
