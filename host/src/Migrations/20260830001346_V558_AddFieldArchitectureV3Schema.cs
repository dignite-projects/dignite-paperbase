using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Vault.Extract.Host.Migrations
{
    /// <inheritdoc />
    public partial class V558_AddFieldArchitectureV3Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlexFields",
                table: "VaultDocuments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "VaultDocumentFlexFieldIndexes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    StringValue = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    NumberValue = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: true),
                    DateTimeValue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    GuidValue = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultDocumentFlexFieldIndexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaultDocumentFlexFieldIndexes_VaultDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "VaultDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VaultFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FieldTypeName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Configuration = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsSearchable = table.Column<bool>(type: "bit", nullable: false),
                    IsUniqueKey = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaultFields_VaultDocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "VaultDocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentFlexFieldIndexes_DocumentId",
                table: "VaultDocumentFlexFieldIndexes",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentFlexFieldIndexes_TenantId_FieldId_DateTimeValue_DocumentId",
                table: "VaultDocumentFlexFieldIndexes",
                columns: new[] { "TenantId", "FieldId", "DateTimeValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentFlexFieldIndexes_TenantId_FieldId_GuidValue_DocumentId",
                table: "VaultDocumentFlexFieldIndexes",
                columns: new[] { "TenantId", "FieldId", "GuidValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentFlexFieldIndexes_TenantId_FieldId_NumberValue_DocumentId",
                table: "VaultDocumentFlexFieldIndexes",
                columns: new[] { "TenantId", "FieldId", "NumberValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultDocumentFlexFieldIndexes_TenantId_FieldId_StringValue_DocumentId",
                table: "VaultDocumentFlexFieldIndexes",
                columns: new[] { "TenantId", "FieldId", "StringValue", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaultFields_DocumentTypeId",
                table: "VaultFields",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VaultFields_TenantId_DocumentTypeId",
                table: "VaultFields",
                columns: new[] { "TenantId", "DocumentTypeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VaultDocumentFlexFieldIndexes");

            migrationBuilder.DropTable(
                name: "VaultFields");

            migrationBuilder.DropColumn(
                name: "FlexFields",
                table: "VaultDocuments");
        }
    }
}
