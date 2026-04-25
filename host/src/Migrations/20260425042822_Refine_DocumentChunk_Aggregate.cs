using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Refine_DocumentChunk_Aggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaperbaseDocumentChunks_DocumentId",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "PaperbaseDocumentChunks",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "PaperbaseDocumentChunks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                table: "PaperbaseDocumentChunks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                table: "PaperbaseDocumentChunks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraProperties",
                table: "PaperbaseDocumentChunks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChunks_DocumentId_ChunkIndex",
                table: "PaperbaseDocumentChunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId",
                table: "PaperbaseDocumentChunks",
                column: "DocumentId",
                principalTable: "PaperbaseDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // pgvector HNSW ANN index for SearchByVectorAsync.
            // Requires pgvector >= 0.5.0. ANN over cosine distance.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_PaperbaseDocumentChunks_EmbeddingVector_HNSW""
                ON ""PaperbaseDocumentChunks"" USING hnsw (""EmbeddingVector"" vector_cosine_ops);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaperbaseDocumentChunks_EmbeddingVector_HNSW"";");

            migrationBuilder.DropForeignKey(
                name: "FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropIndex(
                name: "IX_PaperbaseDocumentChunks_DocumentId_ChunkIndex",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "ExtraProperties",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "PaperbaseDocumentChunks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChunks_DocumentId",
                table: "PaperbaseDocumentChunks",
                column: "DocumentId");
        }
    }
}
