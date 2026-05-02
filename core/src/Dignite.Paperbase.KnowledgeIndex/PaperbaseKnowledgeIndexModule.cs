using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.KnowledgeIndex;

public class PaperbaseKnowledgeIndexModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<PaperbaseKnowledgeIndexOptions>()
            .BindConfiguration("PaperbaseKnowledgeIndex");
    }
}
