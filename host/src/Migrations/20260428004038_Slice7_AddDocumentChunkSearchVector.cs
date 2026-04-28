using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class Slice7_AddDocumentChunkSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var chunkTable = DelimitIdentifier(
                PaperbaseDbProperties.DbTablePrefix + "DocumentChunks",
                PaperbaseDbProperties.DbSchema);
            var searchVectorIndex = DelimitIdentifier(
                "IX_" + PaperbaseDbProperties.DbTablePrefix + "DocumentChunks_SearchVector",
                PaperbaseDbProperties.DbSchema);

            // Slice 7 — Hybrid search (pgvector + tsvector + RRF).
            //
            // Adds a generated tsvector column on PaperbaseDocumentChunks for the
            // BM25/keyword recall path. The column is GENERATED ALWAYS ... STORED,
            // so PostgreSQL maintains it on every INSERT/UPDATE — no app-side write,
            // no trigger to keep in sync. The column is intentionally invisible to
            // EF Core: PgvectorDocumentVectorStore queries it via raw SQL, and the
            // model snapshot does not declare it (subsequent migrations will not
            // try to drop it).
            //
            // Regconfig choice: 'simple'.
            //   - Paperbase documents are mixed JP/EN with many IDs (contract /
            //     invoice / certificate numbers), names, dates, and amounts — exactly
            //     the recall failures hybrid is meant to fix. 'english' would stem
            //     and drop stop-words; 'simple' preserves all tokens and just
            //     lowercases. PostgreSQL has no built-in Japanese tokenizer, so
            //     'simple' is also the safest baseline for JP corpora.
            //
            // Cost note: ADD COLUMN ... GENERATED ALWAYS AS ... STORED requires a
            // table rewrite and holds an ACCESS EXCLUSIVE lock — the table is
            // unreadable and unwriteable for the rewrite duration. For Paperbase's
            // current low-volume state this is acceptable during a normal deploy.
            //
            // Pre-deploy checklist (operators):
            //   1. Confirm row count of PaperbaseDocumentChunks is small enough to
            //      tolerate the lock window (rule of thumb: < 500k rows on commodity
            //      Postgres takes seconds; > a few million rows needs scheduling).
            //   2. If the chunk table has grown beyond that, abort this migration and
            //      switch to a multi-step pattern: nullable column → batched UPDATE
            //      backfill → trigger or ALTER COLUMN ... GENERATED.
            //   3. After deploy, run EXPLAIN ANALYZE on a hybrid query to confirm the
            //      Bitmap Index Scan on IX_PaperbaseDocumentChunks_SearchVector is hit.
            //
            // Regconfig symmetry: 'simple' must match the regconfig used by the query
            // path in PgvectorDocumentVectorStore.SearchKeywordAsync. If they diverge,
            // the GIN index will silently not be used and queries fall back to seq scan.
            migrationBuilder.Sql($@"
                ALTER TABLE {chunkTable}
                ADD COLUMN ""SearchVector"" tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', ""ChunkText"")) STORED;
            ");

            migrationBuilder.Sql($@"
                CREATE INDEX {searchVectorIndex}
                ON {chunkTable}
                USING GIN (""SearchVector"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var chunkTable = DelimitIdentifier(
                PaperbaseDbProperties.DbTablePrefix + "DocumentChunks",
                PaperbaseDbProperties.DbSchema);
            var searchVectorIndex = DelimitIdentifier(
                "IX_" + PaperbaseDbProperties.DbTablePrefix + "DocumentChunks_SearchVector",
                PaperbaseDbProperties.DbSchema);

            // DROP order: index first, then column. IF EXISTS on both makes Down
            // re-runnable if a partial rollback already happened.
            migrationBuilder.Sql($@"DROP INDEX IF EXISTS {searchVectorIndex};");
            migrationBuilder.Sql($@"ALTER TABLE {chunkTable} DROP COLUMN IF EXISTS ""SearchVector"";");
        }

        private static string DelimitIdentifier(string name, string schema = null)
        {
            var delimitedName = "\"" + name.Replace("\"", "\"\"") + "\"";
            return schema == null
                ? delimitedName
                : "\"" + schema.Replace("\"", "\"\"") + "\"." + delimitedName;
        }
    }
}
