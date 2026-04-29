using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice5_DropPipelineRunResultCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultCode",
                table: "PaperbaseDocumentPipelineRuns");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "PaperbaseDocumentPipelineRuns",
                newName: "StatusMessage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StatusMessage",
                table: "PaperbaseDocumentPipelineRuns",
                newName: "ErrorMessage");

            migrationBuilder.AddColumn<string>(
                name: "ResultCode",
                table: "PaperbaseDocumentPipelineRuns",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }
    }
}
