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
            // Historical no-op: this migration once extended relational chunk storage.
            // Qdrant collection schema is ensured by QdrantCollectionInitializer at startup.
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；FK 由 cutover SQL 删除。
            //   - 全新部署：no-op；Qdrant collection schema 由启动初始化器创建。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
