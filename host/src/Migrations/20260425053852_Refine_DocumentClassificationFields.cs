using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Refine_DocumentClassificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StructuredData",
                table: "PaperbaseDocuments");

            migrationBuilder.RenameColumn(
                name: "ConfidenceScore",
                table: "PaperbaseDocuments",
                newName: "ClassificationConfidence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ClassificationConfidence",
                table: "PaperbaseDocuments",
                newName: "ConfidenceScore");

            migrationBuilder.AddColumn<string>(
                name: "StructuredData",
                table: "PaperbaseDocuments",
                type: "text",
                nullable: true);
        }
    }
}
