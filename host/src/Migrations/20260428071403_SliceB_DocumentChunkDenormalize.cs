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
            // Historical no-op: this migration once added denormalized fields to
            // relational chunk storage. Qdrant stores these values as payload fields,
            // and QdrantCollectionInitializer ensures the required payload indexes.
            //
            //   - 现有部署：MigrationId 在主 __EFMigrationsHistory，EF 跳过 Up()；列、索引、数据已就位。
            //   - 全新部署：no-op；Qdrant collection schema 由启动初始化器创建。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
