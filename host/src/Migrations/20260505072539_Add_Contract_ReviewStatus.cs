using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Add_Contract_ReviewStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "PaperbaseContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE [PaperbaseContracts]
                SET [ReviewStatus] = CASE WHEN [NeedsReview] = 1 THEN 0 ELSE 1 END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_ReviewStatus",
                table: "PaperbaseContracts",
                column: "ReviewStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaperbaseContracts_ReviewStatus",
                table: "PaperbaseContracts");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "PaperbaseContracts");
        }
    }
}
