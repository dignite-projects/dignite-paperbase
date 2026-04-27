using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Documents.AI;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Application;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Mapperly;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseAbstractionsModule),
    typeof(PaperbaseDomainModule),
    typeof(PaperbaseApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpBackgroundJobsModule),
    typeof(AbpMapperlyModule)
    )]
public class PaperbaseApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<PaperbaseApplicationModule>();

        // 把"配置维度 == DB schema 维度"作为启动期不变量校验，避免上线后向量 INSERT/查询才报错。
        // 切换 embedding 模型时必须同步更新 PaperbaseDbProperties.EmbeddingVectorDimension 并新增 Migration。
        context.Services
            .AddOptions<PaperbaseAIOptions>()
            .Validate(o => o.EmbeddingVectorDimension == PaperbaseDbProperties.EmbeddingVectorDimension,
                $"PaperbaseAI:EmbeddingVectorDimension must equal PaperbaseDbProperties.EmbeddingVectorDimension " +
                $"({PaperbaseDbProperties.EmbeddingVectorDimension}). " +
                $"Switching the embedding model requires updating both values and generating a new EF Core migration.")
            .ValidateOnStart();
    }
}
