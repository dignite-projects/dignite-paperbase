using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Threading;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

[DependsOn(typeof(PaperbaseKnowledgeIndexModule))]
public class QdrantKnowledgeIndexModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<QdrantKnowledgeIndexOptions>()
            .BindConfiguration("QdrantKnowledgeIndex")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Endpoint), "QdrantKnowledgeIndex:Endpoint is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.CollectionName), "QdrantKnowledgeIndex:CollectionName is required.")
            .Validate(o => o.VectorDimension > 0, "QdrantKnowledgeIndex:VectorDimension must be greater than zero.")
            .Validate(o => o.Distance.Trim().Equals("Cosine", System.StringComparison.OrdinalIgnoreCase),
                "QdrantKnowledgeIndex:Distance currently supports only 'Cosine'.")
            .ValidateOnStart();
    }

    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<PaperbaseKnowledgeIndexOptions>()
            .Validate<IOptions<QdrantKnowledgeIndexOptions>>(
                (kix, qdrant) => kix.EmbeddingDimension == qdrant.Value.VectorDimension,
                "PaperbaseKnowledgeIndex:EmbeddingDimension must equal QdrantKnowledgeIndex:VectorDimension.")
            .ValidateOnStart();
    }

    public override void OnPostApplicationInitialization(ApplicationInitializationContext context)
    {
        AsyncHelper.RunSync(async () =>
        {
            using var scope = context.ServiceProvider.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<QdrantCollectionInitializer>()
                .EnsureAsync();
        });
    }
}
