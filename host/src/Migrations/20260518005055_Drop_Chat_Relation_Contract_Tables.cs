using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Drop_Chat_Relation_Contract_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperbaseChatMessages");

            migrationBuilder.DropTable(
                name: "PaperbaseContracts");

            migrationBuilder.DropTable(
                name: "PaperbaseDocumentRelations");

            migrationBuilder.DropTable(
                name: "PaperbaseChatConversations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperbaseChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperbaseContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutoRenewal = table.Column<bool>(type: "bit", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractionConfidence = table.Column<double>(type: "float", nullable: true),
                    GoverningLaw = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    NormalizedContractNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NormalizedPartyAName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedPartyBName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PartyAName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PartyBName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    SignedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TerminationNoticeDays = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperbaseDocumentRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseDocumentRelations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperbaseChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientTurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDegraded = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperbaseChatMessages_PaperbaseChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "PaperbaseChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseChatConversations_TenantId_CreatorId_CreationTime",
                table: "PaperbaseChatConversations",
                columns: new[] { "TenantId", "CreatorId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseChatMessages_ConversationId_ClientTurnId",
                table: "PaperbaseChatMessages",
                columns: new[] { "ConversationId", "ClientTurnId" },
                unique: true,
                filter: "[ClientTurnId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseChatMessages_ConversationId_CreationTime",
                table: "PaperbaseChatMessages",
                columns: new[] { "ConversationId", "CreationTime" });

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
                name: "IX_PaperbaseContracts_NormalizedContractNumber",
                table: "PaperbaseContracts",
                column: "NormalizedContractNumber",
                filter: "NormalizedContractNumber IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_NormalizedPartyAName_NormalizedPartyBName",
                table: "PaperbaseContracts",
                columns: new[] { "NormalizedPartyAName", "NormalizedPartyBName" },
                filter: "NormalizedPartyAName IS NOT NULL AND NormalizedPartyBName IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_ReviewStatus",
                table: "PaperbaseContracts",
                column: "ReviewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseContracts_Status",
                table: "PaperbaseContracts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentRelations_SourceDocumentId",
                table: "PaperbaseDocumentRelations",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentRelations_TargetDocumentId",
                table: "PaperbaseDocumentRelations",
                column: "TargetDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentRelations_TenantId_SourceDocumentId_TargetDocumentId",
                table: "PaperbaseDocumentRelations",
                columns: new[] { "TenantId", "SourceDocumentId", "TargetDocumentId" },
                unique: true,
                filter: "IsDeleted = 0");
        }
    }
}
