using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseDomainModule),
    typeof(PaperbaseTestBaseModule)
)]
public class PaperbaseDomainTestModule : AbpModule
{
    // DocumentPipelineRunManager 不再依赖 IDocumentTypeRepository——typeCode 校验
    // 责任已经移到 AppService 层（调用方先 load DocumentType 再传 manager）。
    // Domain.Tests 无需 mock 仓储，测试自己 new DocumentType 实例传给 manager 即可。
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
    }
}
