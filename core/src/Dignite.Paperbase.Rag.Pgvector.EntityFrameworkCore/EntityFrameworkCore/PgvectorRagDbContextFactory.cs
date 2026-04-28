using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;

/// <summary>
/// 设计时工厂：让 <c>dotnet ef migrations add ...</c> 可以在本 EF Core 项目目录下直接生成
/// <see cref="PgvectorRagDbContext"/> 的迁移文件。
///
/// <para>
/// EF Core 设计时管线 <b>不会</b> 经过 ABP 的依赖注入，因此本工厂必须自行：
/// <list type="number">
///   <item><description>读取 host 项目的 <c>appsettings*.json</c>，取 <c>PaperbaseRag</c>
///     connection string（缺失时 fallback 到 <c>Default</c>，仅供设计时使用）；</description></item>
///   <item><description>显式 <c>UseNpgsql(b =&gt; { b.UseVector(); b.MigrationsHistoryTable(...); })</c>，
///     与 <see cref="PgvectorRagEntityFrameworkCoreModule"/> 的运行时配置保持一致——任何一处遗漏
///     <c>MigrationsHistoryTable</c> 都会让设计时和运行时使用不同的 history 表。</description></item>
/// </list>
/// </para>
/// </summary>
public class PgvectorRagDbContextFactory : IDesignTimeDbContextFactory<PgvectorRagDbContext>
{
    public PgvectorRagDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();

        // 严格匹配 PaperbaseRag——不 fallback 到 Default，避免设计时把迁移静默生成到主库。
        // 仅在 host appsettings 都不可用时使用本地默认串作为"刚 clone 仓库就能跑命令"的兜底。
        var connectionString =
            configuration.GetConnectionString(PgvectorRagDbProperties.ConnectionStringName)
            ?? "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<PgvectorRagDbContext>()
            .UseNpgsql(connectionString, b =>
            {
                b.UseVector();
                b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
            });

        return new PgvectorRagDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        // 路径：core/src/Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore → host/src
        var hostBasePath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "host", "src"));

        return new ConfigurationBuilder()
            .SetBasePath(hostBasePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
