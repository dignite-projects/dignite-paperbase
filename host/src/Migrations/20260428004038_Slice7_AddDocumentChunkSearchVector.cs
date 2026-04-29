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
            // Historical no-op: this migration once added relational full-text columns
            // for hybrid search. Qdrant first phase does not use relational SearchVector
            // columns; collection schema is ensured by QdrantCollectionInitializer.
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；列与索引早已存在。
            //   - 全新部署：no-op；Qdrant collection schema 由启动初始化器创建。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
