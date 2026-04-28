using Dignite.Paperbase.EntityFrameworkCore;
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
/// 否则两个 DbContext 共用默认 <c>__EFMigrationsHistory</c> 表，让 Slice D 的 cutover
/// 路径与实际行为对不上。
/// </para>
///
/// <para>
/// 依赖 <see cref="PaperbaseEntityFrameworkCoreModule"/> 是 Slice C 的过渡安排——
/// chunks 表的物理迁移记录目前仍在主 host migrations 中（Slice D 才会转移），且
/// host 上配置的 <c>AbpDbContextOptions</c> 默认 provider 也由主 EF Core 模块栈带入。
/// 本模块同时复写 per-context 的 <c>PgvectorRagDbContext</c> options，使其指向
/// <c>PaperbaseRag</c> connection string 并启用 pgvector + 独立 history 表。
/// </para>
/// </summary>
[DependsOn(
    typeof(PgvectorRagDomainModule),
    typeof(AbpEntityFrameworkCoreModule),
    typeof(PaperbaseEntityFrameworkCoreModule))]
public class PgvectorRagEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PgvectorRagDbContext>(options =>
        {
            // 显式注册自定义 chunk repository。EfCoreDocumentChunkRepository 是 IDocumentChunkRepository
            // 唯一的实现，所有上层（DocumentRelationInferenceBackgroundJob、Embedding job 等）都通过
            // IDocumentChunkRepository 访问，因此一定走 PgvectorRagDbContext。
            //
            // 注意：host 上的 PaperbaseHostDbContext 通过 ConfigurePaperbase() 把 DocumentChunk 也带入
            // 了它的 model（用于 migration 聚合），并以 AddDefaultRepositories(includeAllEntities: true)
            // 注册了一遍 IRepository<DocumentChunk, Guid>——按 DI 后注册者优先的语义，
            // 通用 IRepository<DocumentChunk, Guid> 的解析结果会落到 PaperbaseHostDbContext。
            // 本仓库目前**没有任何代码**消费通用 IRepository<DocumentChunk, Guid>（grep 已确认零命中），
            // 因此实际写入路径不受影响。但请勿将来在 Application 层 inject 通用 repo——一旦那么做，
            // 写入会落到主 connection string 而非 PaperbaseRag。Slice D 把主 context 的 chunk mapping
            // 彻底移除后，这个隐性风险窗口自动关闭。
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
                    // Slice D 的 cutover SQL 会引用同名常量。
                    b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
                });
            });
        });
    }
}
