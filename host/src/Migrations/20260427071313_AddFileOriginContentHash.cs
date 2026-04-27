using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddFileOriginContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileOrigin_ContentHash",
                table: "PaperbaseDocuments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // 每租户字节级去重；NULLS NOT DISTINCT (PG 15+) 让单租户场景下 TenantId=NULL 行也参与判重。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_PaperbaseDocuments_TenantId_FileOrigin_ContentHash\" " +
                "ON \"PaperbaseDocuments\" (\"TenantId\", \"FileOrigin_ContentHash\") NULLS NOT DISTINCT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_PaperbaseDocuments_TenantId_FileOrigin_ContentHash\";");

            migrationBuilder.DropColumn(
                name: "FileOrigin_ContentHash",
                table: "PaperbaseDocuments");
        }
    }
}
