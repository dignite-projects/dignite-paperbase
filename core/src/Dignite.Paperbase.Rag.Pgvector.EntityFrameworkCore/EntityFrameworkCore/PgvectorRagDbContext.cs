using Dignite.Paperbase.Rag.Pgvector.Documents;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;

/// <summary>
/// pgvector-backed RAG provider 的独立 DbContext。承载 <see cref="DocumentChunk"/>
/// 和 <see cref="DocumentVector"/>（document-level mean-pooled embedding）；
/// 不感知 Paperbase 的业务实体（Document / PipelineRun…）。
///
/// <para>
/// <b>分库的物理前置：</b>
/// <list type="bullet">
///   <item><description>connection string name <c>"PaperbaseRag"</c> 与主 <c>"Paperbase"</c> 显式分离，
///     允许部署期把向量数据指向独立实例或独立 cluster。</description></item>
///   <item><description>独立 <c>__EFMigrationsHistory_PgvectorRag</c>（在
///     <see cref="PgvectorRagEntityFrameworkCoreModule"/> 内 <c>UseNpgsql</c> 显式配置），
///     与主库 history 表分离，保证 cutover 安全。</description></item>
/// </list>
/// </para>
/// </summary>
[ConnectionStringName(PgvectorRagDbProperties.ConnectionStringName)]
public class PgvectorRagDbContext : AbpDbContext<PgvectorRagDbContext>
{
    public DbSet<DocumentChunk> DocumentChunks { get; set; } = default!;

    public DbSet<DocumentVector> DocumentVectors { get; set; } = default!;

    public PgvectorRagDbContext(DbContextOptions<PgvectorRagDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigurePgvectorRag(isNpgsql: Database.ProviderName?.Contains("Npgsql") == true);
    }
}
