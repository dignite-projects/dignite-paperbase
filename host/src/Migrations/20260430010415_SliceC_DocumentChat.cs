using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class SliceC_DocumentChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperbaseDocumentChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentTypeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TopK = table.Column<int>(type: "integer", nullable: true),
                    MinScore = table.Column<double>(type: "double precision", nullable: true),
                    AgentSessionJson = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseDocumentChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperbaseDocumentChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CitationsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ClientTurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseDocumentChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperbaseDocumentChatMessages_PaperbaseDocumentChatConversa~",
                        column: x => x.ConversationId,
                        principalTable: "PaperbaseDocumentChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChatConversations_TenantId_CreatorId_Creat~",
                table: "PaperbaseDocumentChatConversations",
                columns: new[] { "TenantId", "CreatorId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChatMessages_ConversationId_ClientTurnId",
                table: "PaperbaseDocumentChatMessages",
                columns: new[] { "ConversationId", "ClientTurnId" },
                unique: true,
                filter: "\"ClientTurnId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChatMessages_ConversationId_CreationTime",
                table: "PaperbaseDocumentChatMessages",
                columns: new[] { "ConversationId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperbaseDocumentChatMessages");

            migrationBuilder.DropTable(
                name: "PaperbaseDocumentChatConversations");
        }
    }
}
