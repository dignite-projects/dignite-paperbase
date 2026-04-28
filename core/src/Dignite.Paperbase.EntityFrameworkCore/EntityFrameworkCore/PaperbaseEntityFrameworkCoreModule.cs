using Dignite.Paperbase.Documents;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.EntityFrameworkCore;

[DependsOn(
    typeof(PaperbaseDomainModule),
    typeof(AbpEntityFrameworkCoreModule)
)]
public class PaperbaseEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PaperbaseDbContext>(options =>
        {
            options.AddDefaultRepositories();

            options.AddRepository<Document, EfCoreDocumentRepository>();
            options.AddRepository<DocumentPipelineRun, EfCoreDocumentPipelineRunRepository>();
            options.AddRepository<DocumentRelation, EfCoreDocumentRelationRepository>();

            // Slice C：DocumentChunk repository 已切到 PgvectorRagEntityFrameworkCoreModule，
            // 不再在主 PaperbaseDbContext 上注册——chunk 的写入路径只走 PgvectorRagDbContext。
        });

    }
}
