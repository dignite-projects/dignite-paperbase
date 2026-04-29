using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice7_AddDocumentChunkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slice D (cutover)：本迁移原本为 PaperbaseDocumentChunks 添加 SearchVector tsvector
            // GENERATED 列与 GIN 索引，用于 hybrid search keyword 路径。chunks 表所有权已迁移到
            // Qdrant collection，SearchVector 列与 GIN 索引由其初始迁移创建。
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；列与索引早已存在。
            //   - 全新部署：no-op；由 Qdrant collection 初始迁移创建。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
