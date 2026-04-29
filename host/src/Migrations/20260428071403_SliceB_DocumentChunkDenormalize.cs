using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class SliceB_DocumentChunkDenormalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slice D (cutover)：本迁移原本在 PaperbaseDocumentChunks 上加反范式化字段
            // (DocumentTypeCode / Title / PageNumber)、backfill 数据、创建复合索引
            // (TenantId, DocumentTypeCode)。chunks 表所有权已迁移到 Qdrant collection，全部反范式化字段
            // 与索引由其初始迁移统一创建；backfill 不再适用（迁移期间 backfill 已完成，新版写路径直接填值）。
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；列、索引、数据已就位。
            //   - 全新部署：no-op；由 Qdrant collection 初始迁移创建。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
