using Dignite.Paperbase.Rag.Pgvector.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;

/// <summary>
/// 注册独立的 <see cref="PgvectorRagDbContext"/>、其 chunk repository 和 pgvector
/// provider 实现。
///
/// <para>
/// 关键约束：<c>UseNpgsql(b =&gt; b.MigrationsHistoryTable(...))</c> 必须显式调用——
/// 否则两个 DbContext 共用默认 <c>__EFMigrationsHistory</c> 表，让 cutover SQL
/// 路径与实际行为对不上。
/// </para>
///
/// <para>
/// Slice D：本模块不再 <c>DependsOn(PaperbaseEntityFrameworkCoreModule)</c>。
/// 主 EF Core 模块零向量依赖；本模块完全独立装配，host 通过 <c>[DependsOn]</c>
/// 同时引入两者。这是跨数据库部署能力的物理前置：未来主 DB 切到 SQL Server 时，
/// 本模块仍可独立 attach 到 PostgreSQL+pgvector 实例。
/// </para>
/// </summary>
[DependsOn(
    typeof(PgvectorRagDomainModule),
    typeof(AbpEntityFrameworkCoreModule))]
public class PgvectorRagEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PgvectorRagDbContext>(options =>
        {
            // 显式注册自定义 chunk repository。EfCoreDocumentChunkRepository 是 IDocumentChunkRepository
            // 唯一的实现，所有上层（DocumentRelationInferenceBackgroundJob、Embedding job 等）都通过
            // IDocumentChunkRepository 访问，因此一定走 PgvectorRagDbContext。
            options.AddRepository<DocumentChunk, EfCoreDocumentChunkRepository>();
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<PgvectorRagDbContext>(opts =>
            {
                opts.UseNpgsql(b =>
                {
                    b.UseVector();
                    // 独立 history 表（重要）：与主 PaperbaseDbContext 默认的 __EFMigrationsHistory 分离，
                    // cutover SQL 引用同名常量。
                    b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
                });
            });
        });
    }
}
