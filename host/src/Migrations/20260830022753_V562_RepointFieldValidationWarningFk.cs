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

            // WITH NOCHECK, not migrationBuilder.AddForeignKey: VaultFields is populated by
            // FieldArchitectureV3Migrator's data pass, which VaultExtractHostDbMigrationService runs only after every
            // schema migration (this one included) has already applied. A plain AddForeignKey validates every
            // existing VaultDocumentFieldValidationWarnings row against VaultFields at ADD time, when the table is
            // still empty - it would fail outright on any database that already has warning rows (#527 has been live
            // since v0.3.0). The constraint still enforces on every write from this point forward; it is simply not
            // re-validated against rows that predate it, which is safe because FieldDefinitionToFieldMapper preserves
            // ids 1:1.
            migrationBuilder.Sql(
                @"ALTER TABLE [VaultDocumentFieldValidationWarnings] WITH NOCHECK
                ADD CONSTRAINT [FK_VaultDocumentFieldValidationWarnings_VaultFields_FieldDefinitionId]
                FOREIGN KEY ([FieldDefinitionId]) REFERENCES [VaultFields] ([Id])
                ON DELETE NO ACTION;");
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
