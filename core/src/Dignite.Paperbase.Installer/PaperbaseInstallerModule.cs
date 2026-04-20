using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(AbpVirtualFileSystemModule)
    )]
public class PaperbaseInstallerModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<PaperbaseInstallerModule>();
        });
    }
}
