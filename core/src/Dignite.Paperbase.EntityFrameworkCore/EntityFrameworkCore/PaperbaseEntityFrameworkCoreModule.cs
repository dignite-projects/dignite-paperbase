using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag.Pgvector.Documents;
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
            options.AddRepository<DocumentChunk, EfCoreDocumentChunkRepository>();
        });

    }
}
