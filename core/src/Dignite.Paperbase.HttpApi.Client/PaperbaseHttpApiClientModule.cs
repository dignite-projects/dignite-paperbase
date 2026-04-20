using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Http.Client;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseApplicationContractsModule),
    typeof(AbpHttpClientModule))]
public class PaperbaseHttpApiClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClientProxies(
            typeof(PaperbaseApplicationContractsModule).Assembly,
            PaperbaseRemoteServiceConsts.RemoteServiceName
        );

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<PaperbaseHttpApiClientModule>();
        });

    }
}
