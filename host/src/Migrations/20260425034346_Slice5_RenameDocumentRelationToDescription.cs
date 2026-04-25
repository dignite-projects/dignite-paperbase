using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice5_RenameDocumentRelationToDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileOrigin_DeviceInfo",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "FileOrigin_ScannedAt",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "FileOrigin_UploadedAt",
                table: "PaperbaseDocuments");

            migrationBuilder.DropColumn(
                name: "FileOrigin_UploadedByUserId",
                table: "PaperbaseDocuments");

            migrationBuilder.RenameColumn(
                name: "RelationType",
                table: "PaperbaseDocumentRelations",
                newName: "Description");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PaperbaseDocumentRelations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "PaperbaseDocumentRelations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExtraProperties",
                table: "PaperbaseDocumentRelations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "PaperbaseDocumentRelations");

            migrationBuilder.DropColumn(
                name: "ExtraProperties",
                table: "PaperbaseDocumentRelations");

            migrationBuilder.AddColumn<string>(
                name: "FileOrigin_DeviceInfo",
                table: "PaperbaseDocuments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FileOrigin_ScannedAt",
                table: "PaperbaseDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FileOrigin_UploadedAt",
                table: "PaperbaseDocuments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "FileOrigin_UploadedByUserId",
                table: "PaperbaseDocuments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PaperbaseDocumentRelations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "PaperbaseDocumentRelations",
                newName: "RelationType");
        }
    }
}
