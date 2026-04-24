using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice3_ContractFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRenewal",
                table: "PaperbaseContracts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoverningLaw",
                table: "PaperbaseContracts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "PaperbaseContracts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminationNoticeDays",
                table: "PaperbaseContracts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRenewal",
                table: "PaperbaseContracts");

            migrationBuilder.DropColumn(
                name: "GoverningLaw",
                table: "PaperbaseContracts");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "PaperbaseContracts");

            migrationBuilder.DropColumn(
                name: "TerminationNoticeDays",
                table: "PaperbaseContracts");
        }
    }
}
