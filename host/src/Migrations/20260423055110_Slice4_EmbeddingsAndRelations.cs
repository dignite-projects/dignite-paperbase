using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice4_EmbeddingsAndRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical no-op: this migration once created relational chunk storage.
            // The current RAG provider stores chunks in Qdrant, so the host DbContext
            // keeps no vector-store tables.
            //
            // The migration id is retained for installations that already applied it.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
