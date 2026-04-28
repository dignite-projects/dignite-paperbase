using Dignite.Paperbase.Rag.Pgvector.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Dignite.Paperbase.Rag;

namespace Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;

/// <summary>
/// 注册独立的 <see cref="PgvectorRagDbContext"/>、chunk / document-vector repositories
/// 和 pgvector provider 实现。
///
/// <para>
/// 关键约束：<c>UseNpgsql(b =&gt; b.MigrationsHistoryTable(...))</c> 必须显式调用——
/// 否则两个 DbContext 共用默认 <c>__EFMigrationsHistory</c> 表。
/// </para>
///
/// <para>
/// Slice D：本模块不再 <c>DependsOn(PaperbaseEntityFrameworkCoreModule)</c>。
/// 主 EF Core 模块零向量依赖；本模块完全独立装配。
/// </para>
/// </summary>
[DependsOn(
    typeof(PgvectorRagDomainModule),
    typeof(AbpEntityFrameworkCoreModule))]
public class PgvectorRagEntityFrameworkCoreModule : AbpModule
{
    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<PaperbaseRagOptions>()
            .Validate(
                o => o.EmbeddingDimension == DocumentChunkConsts.EmbeddingVectorDimension,
                $"PaperbaseRag:EmbeddingDimension must equal DocumentChunkConsts.EmbeddingVectorDimension " +
                $"({DocumentChunkConsts.EmbeddingVectorDimension}). " +
                $"Switching the embedding model requires updating DocumentChunkConsts and generating " +
                $"a new EF Core migration for PgvectorRagDbContext.")
            .ValidateOnStart();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PgvectorRagDbContext>(options =>
        {
            options.AddRepository<DocumentChunk, EfCoreDocumentChunkRepository>();
            options.AddRepository<DocumentVector, EfCoreDocumentVectorRepository>();
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<PgvectorRagDbContext>(opts =>
            {
                opts.UseNpgsql(b =>
                {
                    b.UseVector();
                    b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
                });
            });
        });
    }
}
