using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <summary>
    /// Drops the legacy <c>ExtractedText</c> column on <c>PaperbaseDocuments</c>.
    /// After this migration, <c>Document.Markdown</c> is the single text payload of the
    /// TextExtraction pipeline; downstream readers (classification / embedding / chat /
    /// business modules) consume <c>Markdown</c> directly and project to plain text via
    /// <c>Dignite.Paperbase.Documents.MarkdownStripper.Strip</c>.
    ///
    /// ⚠️ DESTRUCTIVE — DropColumn is irreversible against existing data.
    /// Before applying this migration to any non-empty database, verify the invariant
    /// "every successful TextExtraction Run has populated Markdown" by running:
    ///
    ///   SELECT COUNT(*) FROM PaperbaseDocuments
    ///   WHERE Markdown IS NULL AND ExtractedText IS NOT NULL;
    ///
    /// If the count is greater than zero, backfill those rows first
    /// (e.g. UPDATE PaperbaseDocuments SET Markdown = ExtractedText WHERE Markdown IS NULL)
    /// or accept the data loss.
    /// </summary>
    public partial class Drop_Document_ExtractedText_Column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail-closed guard: abort the migration if any row has ExtractedText set
            // but Markdown null — operator must backfill before the column is dropped.
            // Runs inside the migration transaction, so a successful DropColumn implies
            // all rows already satisfy the post-#78 invariant Markdown != null.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM PaperbaseDocuments
    WHERE Markdown IS NULL AND ExtractedText IS NOT NULL
)
    THROW 51000, 'Drop_Document_ExtractedText_Column aborted: rows have ExtractedText but NULL Markdown. Backfill them first (e.g. UPDATE PaperbaseDocuments SET Markdown = ExtractedText WHERE Markdown IS NULL AND ExtractedText IS NOT NULL) and re-run.', 1;");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "PaperbaseDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "PaperbaseDocuments",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
