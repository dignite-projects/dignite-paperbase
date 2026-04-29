using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Threading;

namespace Dignite.Paperbase.Rag.Qdrant;

[DependsOn(typeof(PaperbaseRagModule))]
public class QdrantRagModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<QdrantRagOptions>()
            .BindConfiguration("QdrantRag")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Endpoint), "QdrantRag:Endpoint is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.CollectionName), "QdrantRag:CollectionName is required.")
            .Validate(o => o.VectorDimension > 0, "QdrantRag:VectorDimension must be greater than zero.")
            .Validate(o => o.Distance.Trim().Equals("Cosine", System.StringComparison.OrdinalIgnoreCase),
                "QdrantRag:Distance currently supports only 'Cosine'.")
            .ValidateOnStart();
    }

    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<PaperbaseRagOptions>()
            .Validate<IOptions<QdrantRagOptions>>(
                (rag, qdrant) => rag.EmbeddingDimension == qdrant.Value.VectorDimension,
                "PaperbaseRag:EmbeddingDimension must equal QdrantRag:VectorDimension.")
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
