using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice5_ClassificationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "PaperbaseDocumentPipelineRuns");

            migrationBuilder.AddColumn<string>(
                name: "ClassificationReason",
                table: "PaperbaseDocuments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassificationReason",
                table: "PaperbaseDocuments");

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "PaperbaseDocumentPipelineRuns",
                type: "text",
                nullable: true);
        }
    }
}
