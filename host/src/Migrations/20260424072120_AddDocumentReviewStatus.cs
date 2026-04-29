using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReviewStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "PaperbaseDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocuments_ReviewStatus",
                table: "PaperbaseDocuments",
                column: "ReviewStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaperbaseDocuments_ReviewStatus",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "PaperbaseDocuments");
        }
    }
}
