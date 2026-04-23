using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice1_Contracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperbaseContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ContractNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PartyAName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PartyBName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CounterpartyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SignedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExtractionConfidence = table.Column<double>(type: "double precision", nullable: true),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseContracts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_CounterpartyName",
                table: "PaperbaseContracts",
                column: "CounterpartyName");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_DocumentId",
                table: "PaperbaseContracts",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_ExpirationDate",
                table: "PaperbaseContracts",
                column: "ExpirationDate");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_Status",
                table: "PaperbaseContracts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperbaseContracts");
        }
    }
}
