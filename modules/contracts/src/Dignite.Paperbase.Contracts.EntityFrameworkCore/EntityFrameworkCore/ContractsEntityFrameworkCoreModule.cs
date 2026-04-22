using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[DependsOn(
    typeof(ContractsDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class ContractsEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<ContractsDbContext>(options =>
        {
            options.AddDefaultRepositories<IContractsDbContext>();
            options.AddRepository<Contract, EfCoreContractRepository>();
        });
    }
}
