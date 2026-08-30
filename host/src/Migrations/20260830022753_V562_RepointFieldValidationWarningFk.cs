using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Vault.Extract.Host.Migrations
{
    /// <inheritdoc />
    public partial class V562_RepointFieldValidationWarningFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VaultDocumentFieldValidationWarnings_VaultFieldDefinitions_FieldDefinitionId",
                table: "VaultDocumentFieldValidationWarnings");

            migrationBuilder.AddForeignKey(
                name: "FK_VaultDocumentFieldValidationWarnings_VaultFields_FieldDefinitionId",
                table: "VaultDocumentFieldValidationWarnings",
                column: "FieldDefinitionId",
                principalTable: "VaultFields",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VaultDocumentFieldValidationWarnings_VaultFields_FieldDefinitionId",
                table: "VaultDocumentFieldValidationWarnings");

            migrationBuilder.AddForeignKey(
                name: "FK_VaultDocumentFieldValidationWarnings_VaultFieldDefinitions_FieldDefinitionId",
                table: "VaultDocumentFieldValidationWarnings",
                column: "FieldDefinitionId",
                principalTable: "VaultFieldDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
