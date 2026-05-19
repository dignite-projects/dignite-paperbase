using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Add_FieldDefinition_DisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 字段架构 v2 后续修补：FieldDefinition 新增 DisplayName 列。
            // 先以 nullable 方式加列 → backfill 现有行 DisplayName = Name（默认用 Name 作占位，
            // admin 后续可在 UI 改成更人类友好的展示名）→ 再改为 NOT NULL。
            // PaperbaseFieldDefinitions 是 admin-managed config 表（规模 < 千行），单次 UPDATE 不会锁表。
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "PaperbaseFieldDefinitions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [PaperbaseFieldDefinitions] SET [DisplayName] = [Name] WHERE [DisplayName] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "PaperbaseFieldDefinitions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "PaperbaseFieldDefinitions");
        }
    }
}
