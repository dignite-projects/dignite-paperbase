using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice4_EmbeddingsAndRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slice D (cutover)：本迁移原本在主 PaperbaseHostDbContext 上创建 PaperbaseDocumentChunks
            // 表与 pgvector 扩展。chunks 表所有权已迁移到 PgvectorRagDbContext
            // (core/src/Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore/Migrations/)，本迁移退化为 no-op：
            //
            //   - 现有部署：本 MigrationId 已记录在主 __EFMigrationsHistory，EF 跳过 Up()。
            //     物理 chunks 表早已存在，由 cutover SQL (host/scripts/migrate-chunks-to-pgvector-context.sql)
            //     转移历史表归属 + 删除 FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId。
            //   - 全新部署：本 Up() no-op；不创建 chunks 表与 vector 扩展。chunks 表由
            //     PgvectorRagDbContext 的初始迁移负责创建（含 vector 扩展、HNSW 索引、tsvector 列等）。
            //
            // 保留迁移文件（而非删除）以维持主 __EFMigrationsHistory 中已记录的 MigrationId 完整性。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
