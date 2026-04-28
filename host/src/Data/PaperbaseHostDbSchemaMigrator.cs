using Dignite.Paperbase.Contracts.EntityFrameworkCore;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Host.Data;

public class PaperbaseHostDbSchemaMigrator : ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public PaperbaseHostDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve DbContexts from IServiceProvider
         * to properly get the connection string of the current tenant
         * in the current scope.
         */

        // 顺序很重要：主 host context 必须先跑——它持有 PaperbaseDocuments 等核心表，
        // 现有部署中 chunks 表的 FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId
        // 由 host/scripts/migrate-chunks-to-pgvector-context.sql 在这之前删除。
        await _serviceProvider
            .GetRequiredService<PaperbaseHostDbContext>()
            .Database
            .MigrateAsync();

        await _serviceProvider
            .GetRequiredService<ContractsDbContext>()
            .Database
            .MigrateAsync();

        // PgvectorRagDbContext 独立 connection string + 独立 __EFMigrationsHistory_PgvectorRag。
        // 现有部署：cutover SQL 已把初始 migration 标记为已应用，本次调用仅做幂等校验；
        // 全新部署：本次调用执行 SliceD_Init_PgvectorRag，创建 chunks 表 + 扩展 + HNSW/GIN 索引。
        await _serviceProvider
            .GetRequiredService<PgvectorRagDbContext>()
            .Database
            .MigrateAsync();
    }
}
