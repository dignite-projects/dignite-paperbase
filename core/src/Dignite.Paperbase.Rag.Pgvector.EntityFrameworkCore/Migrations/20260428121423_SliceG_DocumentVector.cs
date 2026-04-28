using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Dignite.Paperbase.Rag.Pgvector.Migrations
{
    /// <summary>
    /// Slice G: adds PaperbaseDocumentVectors — document-level mean-pooled embeddings.
    /// One row per document; Id == DocumentId.
    ///
    /// <para>
    /// Indexes:
    /// <list type="bullet">
    ///   <item><description>(TenantId, DocumentTypeCode) — for type-scoped similarity queries.</description></item>
    ///   <item><description>HNSW on EmbeddingVector (vector_cosine_ops) — same as chunks table,
    ///     required by SearchSimilarDocumentsAsync cosine distance ordering.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class SliceG_DocumentVector : Migration
    {
        private const string VectorTable = "PaperbaseDocumentVectors";
        private const string HnswIndex = "IX_PaperbaseDocumentVectors_EmbeddingVector_HNSW";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: VectorTable,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentTypeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EmbeddingVector = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_" + VectorTable, x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentVectors_TenantId_DocumentTypeCode",
                table: VectorTable,
                columns: new[] { "TenantId", "DocumentTypeCode" });

            // pgvector HNSW ANN index (vector_cosine_ops) — mirrors the chunks table index.
            // Required by SearchSimilarDocumentsAsync which orders by cosine distance.
            // Requires pgvector >= 0.5.0.
            migrationBuilder.Sql($@"
                CREATE INDEX IF NOT EXISTS ""{HnswIndex}""
                ON ""{VectorTable}"" USING hnsw (""EmbeddingVector"" vector_cosine_ops);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"DROP INDEX IF EXISTS ""{HnswIndex}"";");
            migrationBuilder.DropTable(name: VectorTable);
        }
    }
}
