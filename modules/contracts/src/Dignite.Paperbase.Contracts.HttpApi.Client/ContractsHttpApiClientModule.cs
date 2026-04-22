using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Http.Client;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(ContractsApplicationContractsModule),
    typeof(AbpHttpClientModule))]
public class ContractsHttpApiClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClientProxies(
            typeof(ContractsApplicationContractsModule).Assembly,
            ContractsRemoteServiceConsts.RemoteServiceName
        );

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<ContractsHttpApiClientModule>();
        });

    }
}
