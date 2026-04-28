using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Dignite.Paperbase.Rag.Pgvector.Migrations
{
    /// <summary>
    /// PgvectorRagDbContext 的初始迁移，承载 Slice D cutover 后 chunks 表的完整 schema。
    ///
    /// <para>
    /// <b>等价性保证（Schema equivalence proof）</b>：本迁移在全新部署上创建的 chunks 表与
    /// 现有部署经历过 Slice 4 / Refine_DocumentChunk_Aggregate / Slice 7 / Slice B 累积演化
    /// 的最终 schema 必须等价。所以本迁移要包含：
    /// <list type="bullet">
    ///   <item><description>所有列（包含 Slice B 反范式化的 DocumentTypeCode / Title / PageNumber，
    ///     以及 ABP <c>ConfigureByConvention</c> 带入的审计列）。</description></item>
    ///   <item><description><c>EmbeddingVector</c> <c>vector(1536)</c> 列。</description></item>
    ///   <item><description>(DocumentId, ChunkIndex) 唯一索引、(TenantId, DocumentTypeCode) 复合索引。</description></item>
    ///   <item><description>HNSW ANN 索引（pgvector）：raw SQL 创建，模型未声明。</description></item>
    ///   <item><description>GENERATED <c>SearchVector</c> <c>tsvector</c> 列 + GIN 索引：raw SQL 创建，
    ///     EF 模型不声明（hybrid search keyword 路径，<c>'simple'</c> regconfig）。</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>不包含</b>：
    /// <list type="bullet">
    ///   <item><description>chunks → documents 的外键（<c>FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId</c>）。
    ///     Slice D 的目标之一就是断开跨 DbContext 外键以打开跨数据库部署能力，
    ///     Document 删除清理 chunks 改由 Slice E 的事件驱动方案实现。</description></item>
    ///   <item><description>Slice 4 老版本里的 ivfflat 索引——HNSW 已经替代它。</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>现有部署执行模式</b>：cutover SQL <c>host/scripts/migrate-chunks-to-pgvector-context.sql</c>
    /// 在部署前把本 MigrationId 写入独立的 <c>__EFMigrationsHistory_PgvectorRag</c> 表，
    /// 应用启动后 EF 跳过 <c>Up()</c>——避免对早已存在的 chunks 表重复创建。
    /// </para>
    /// </summary>
    public partial class SliceD_Init_PgvectorRag : Migration
    {
        private const string ChunkTable = "PaperbaseDocumentChunks";
        private const string SearchVectorIndex = "IX_PaperbaseDocumentChunks_SearchVector";
        private const string HnswIndex = "IX_PaperbaseDocumentChunks_EmbeddingVector_HNSW";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 启用 vector 扩展。Npgsql provider 翻译为 CREATE EXTENSION IF NOT EXISTS "vector"，
            // 因此对现有部署（已启用扩展）幂等。
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: ChunkTable,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkText = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    EmbeddingVector = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_" + ChunkTable, x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChunks_DocumentId_ChunkIndex",
                table: ChunkTable,
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChunks_TenantId_DocumentTypeCode",
                table: ChunkTable,
                columns: new[] { "TenantId", "DocumentTypeCode" });

            // SearchVector tsvector GENERATED 列 + GIN 索引（hybrid search keyword 路径）。
            // - GENERATED ALWAYS ... STORED：由 PostgreSQL 在 INSERT/UPDATE 时自动维护，应用层无需写入。
            // - regconfig 'simple'：保留所有 token 仅做 lowercase。匹配 PgvectorDocumentVectorStore.SearchKeywordAsync
            //   查询路径同样使用的 'simple' regconfig；任一处偏离都会让 GIN 索引失效。
            // - EF 模型不声明此列与索引，纯 raw SQL；后续 EF migration diff 不会触碰它们。
            migrationBuilder.Sql(@"
                ALTER TABLE ""PaperbaseDocumentChunks""
                ADD COLUMN ""SearchVector"" tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', ""ChunkText"")) STORED;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_PaperbaseDocumentChunks_SearchVector""
                ON ""PaperbaseDocumentChunks""
                USING GIN (""SearchVector"");
            ");

            // pgvector HNSW ANN 索引（vector_cosine_ops，与 SearchByVectorAsync 余弦距离对齐）。
            // 要求 pgvector >= 0.5.0。
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_PaperbaseDocumentChunks_EmbeddingVector_HNSW""
                ON ""PaperbaseDocumentChunks"" USING hnsw (""EmbeddingVector"" vector_cosine_ops);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DropTable 也会顺带回收 SearchVector 列与所有索引；保留显式的索引 / 列 DROP 让 Down
            // 在 partial rollback 后仍然幂等。
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaperbaseDocumentChunks_EmbeddingVector_HNSW"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaperbaseDocumentChunks_SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""PaperbaseDocumentChunks"" DROP COLUMN IF EXISTS ""SearchVector"";");

            migrationBuilder.DropTable(
                name: ChunkTable);
        }
    }
}
