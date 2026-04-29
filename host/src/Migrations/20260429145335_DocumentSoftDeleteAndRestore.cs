using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSoftDeleteAndRestore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "PaperbaseDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "PaperbaseDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PaperbaseDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "PaperbaseDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "PaperbaseDocuments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "PaperbaseDocuments");
        }
    }
}
