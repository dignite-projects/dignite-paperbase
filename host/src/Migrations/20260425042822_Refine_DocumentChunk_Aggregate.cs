using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Refine_DocumentChunk_Aggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slice D (cutover)：本迁移原本扩展 PaperbaseDocumentChunks 列、创建 (DocumentId, ChunkIndex)
            // 唯一索引、添加 FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId、创建 HNSW 向量索引。
            // chunks 表所有权已迁移到 Qdrant collection，全部 schema 由其初始迁移统一创建。
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；FK 由 cutover SQL 删除。
            //   - 全新部署：no-op；chunks schema 由 Qdrant collection 初始迁移创建（无跨 context FK）。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
