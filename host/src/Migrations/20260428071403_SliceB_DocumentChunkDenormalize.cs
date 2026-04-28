using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <inheritdoc />
    public partial class SliceB_DocumentChunkDenormalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentTypeCode",
                table: "PaperbaseDocumentChunks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "PaperbaseDocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "PaperbaseDocumentChunks",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // 反范式化字段 backfill：把 Document.DocumentTypeCode 复制到所有现有 chunk 行。
            //
            // 幂等性：只更新 c."DocumentTypeCode" IS NULL 的行，重跑（含部分失败重启）安全；
            // 已被 embedding 重写填值或后续来源不同的行不会被覆盖。
            //
            // 并发插入：UPDATE ... FROM 在 PG READ COMMITTED 下持单语句快照——backfill 运行期间
            // 由旧版应用插入的新 chunk 行不在 snapshot 内，会保持 DocumentTypeCode = NULL。
            // **运维提示**：滚动部署时建议短暂暂停 embedding pipeline；停机部署无此问题。新版写路径
            // (DocumentEmbeddingBackgroundJob → PgvectorDocumentVectorStore.UpsertAsync) 会在
            // 写入时直接填值，所以新版上线后插入的 chunk 不依赖此 backfill。
            //
            // 性能注：DocumentChunks 当前最大量级（万级）下单 UPDATE 毫秒级；接近百万级时改为
            // batch UPDATE ... WHERE "Id" IN (SELECT ... LIMIT N) 分批执行避免长事务。
            //
            // Title / PageNumber 不 backfill：当前 embedding pipeline 暂未生成 chunk-level
            // title / page，旧记录保持 NULL 即可，未来增强会通过重新 embedding 写入。
            migrationBuilder.Sql(@"
                UPDATE ""PaperbaseDocumentChunks"" AS c
                SET ""DocumentTypeCode"" = d.""DocumentTypeCode""
                FROM ""PaperbaseDocuments"" AS d
                WHERE c.""DocumentId"" = d.""Id""
                  AND c.""DocumentTypeCode"" IS NULL
                  AND d.""DocumentTypeCode"" IS NOT NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseDocumentChunks_TenantId_DocumentTypeCode",
                table: "PaperbaseDocumentChunks",
                columns: new[] { "TenantId", "DocumentTypeCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaperbaseDocumentChunks_TenantId_DocumentTypeCode",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "DocumentTypeCode",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "PaperbaseDocumentChunks");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "PaperbaseDocumentChunks");
        }
    }
}
