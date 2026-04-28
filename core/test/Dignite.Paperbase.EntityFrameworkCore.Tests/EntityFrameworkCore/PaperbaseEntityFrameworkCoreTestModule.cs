using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.EntityFrameworkCore;

[DependsOn(
    typeof(PaperbaseApplicationTestModule),
    typeof(PaperbaseEntityFrameworkCoreModule),
    typeof(PgvectorRagEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class PaperbaseEntityFrameworkCoreTestModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        var sqliteConnection = CreateDatabaseAndGetConnection();

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(configurationContext =>
            {
                configurationContext.UseSqlite(sqliteConnection);
            });

            // PgvectorRagEntityFrameworkCoreModule 默认把 PgvectorRagDbContext 配成 Npgsql + UseVector，
            // 在 SQLite in-memory 测试里必须显式覆写为 SQLite。两个 context 共用同一个连接
            // 即可在 ABP UoW 内共用事务，不需要再为 PaperbaseRag 单独配 connection string。
            options.Configure<PgvectorRagDbContext>(configurationContext =>
            {
                configurationContext.UseSqlite(sqliteConnection);
            });
        });
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // 主 PaperbaseDbContext 创建除 chunks 之外的全部表。Slice D 后 chunks 已彻底
        // 移交 PgvectorRagDbContext，主 context 模型不再包含 DocumentChunk，
        // CreateTables() 自然只生成业务表。
        new PaperbaseDbContext(
            new DbContextOptionsBuilder<PaperbaseDbContext>().UseSqlite(connection).Options
        ).GetService<IRelationalDatabaseCreator>().CreateTables();

        // 独立 PgvectorRagDbContext 仅创建 chunks 表 + 索引，与上一行互不重叠。
        new PgvectorRagDbContext(
            new DbContextOptionsBuilder<PgvectorRagDbContext>().UseSqlite(connection).Options
        ).GetService<IRelationalDatabaseCreator>().CreateTables();

        return connection;
    }
}
