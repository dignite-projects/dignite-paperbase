using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Http.Client;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(PaperbaseContractsApplicationContractsModule),
    typeof(AbpHttpClientModule))]
public class PaperbaseContractsHttpApiClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClientProxies(
            typeof(PaperbaseContractsApplicationContractsModule).Assembly,
            PaperbaseContractsRemoteServiceConsts.RemoteServiceName
        );

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<PaperbaseContractsHttpApiClientModule>();
        });

    }
}
