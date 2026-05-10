using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Add_DocumentIdentifier_Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperbaseDocumentIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentifierType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdentifierValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseDocumentIdentifiers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentIdentifiers_DocumentId",
                table: "PaperbaseDocumentIdentifiers",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentIdentifiers_DocumentId_IdentifierType_IdentifierValue",
                table: "PaperbaseDocumentIdentifiers",
                columns: new[] { "DocumentId", "IdentifierType", "IdentifierValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentIdentifiers_IdentifierType_IdentifierValue",
                table: "PaperbaseDocumentIdentifiers",
                columns: new[] { "IdentifierType", "IdentifierValue" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperbaseDocumentIdentifiers");
        }
    }
}
