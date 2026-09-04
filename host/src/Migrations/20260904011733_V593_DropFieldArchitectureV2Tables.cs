using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Vault.Extract.Host.Migrations
{
    /// <inheritdoc />
    public partial class V593_DropFieldArchitectureV2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VaultDocumentExtractedFields");

            migrationBuilder.DropTable(
                name: "VaultFieldDefinitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaultFieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowMultiple = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DataType = table.Column<int>(type: "int", nullable: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsUniqueKey = table.Column<bool>(type: "bit", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultFieldDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaultFieldDefinitions_VaultDocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "VaultDocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VaultDocumentExtractedFields",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    DateTimeValue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    LongTextValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NumberValue = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultDocumentExtractedFields", x => new { x.DocumentId, x.FieldDefinitionId, x.Order });
                    table.ForeignKey(
                        name: "FK_VaultDocumentExtractedFields_VaultDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "VaultDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VaultDocumentExtractedFields_VaultFieldDefinitions_FieldDefinitionId",
                        column: x => x.FieldDefinitionId,
                        principalTable: "VaultFieldDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentExtractedFields_FieldDefinitionId",
                table: "VaultDocumentExtractedFields",
                column: "FieldDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentExtractedFields_TenantId_FieldDefinitionId_DateTimeValue_DocumentId",
                table: "VaultDocumentExtractedFields",
                columns: new[] { "TenantId", "FieldDefinitionId", "DateTimeValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentExtractedFields_TenantId_FieldDefinitionId_DateValue_DocumentId",
                table: "VaultDocumentExtractedFields",
                columns: new[] { "TenantId", "FieldDefinitionId", "DateValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentExtractedFields_TenantId_FieldDefinitionId_NumberValue_DocumentId",
                table: "VaultDocumentExtractedFields",
                columns: new[] { "TenantId", "FieldDefinitionId", "NumberValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentExtractedFields_TenantId_FieldDefinitionId_TextValue_DocumentId",
                table: "VaultDocumentExtractedFields",
                columns: new[] { "TenantId", "FieldDefinitionId", "TextValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultFieldDefinitions_DocumentTypeId",
                table: "VaultFieldDefinitions",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultFieldDefinitions_TenantId_DocumentTypeId",
                table: "VaultFieldDefinitions",
                columns: new[] { "TenantId", "DocumentTypeId" });
        }
    }
}
