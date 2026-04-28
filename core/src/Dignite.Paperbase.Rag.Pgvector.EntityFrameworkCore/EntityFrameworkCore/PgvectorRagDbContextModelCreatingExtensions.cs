using System;
using System.Linq;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag.Pgvector;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;

/// <summary>
/// pgvector RAG provider 的实体映射。Slice C 期间 chunk mapping 在主 <c>PaperbaseDbContext</c>
/// 与本 <c>PgvectorRagDbContext</c> 同时存在——主 context 内已通过
/// <c>ToTable(...).ExcludeFromMigrations()</c> 排除，确保 host migration diff 不会重复创建。
/// 写入路径只走本 context（参见 <see cref="PgvectorRagEntityFrameworkCoreModule"/> 中的
/// <c>AddRepository&lt;DocumentChunk, EfCoreDocumentChunkRepository&gt;</c>）。
/// </summary>
public static class PgvectorRagDbContextModelCreatingExtensions
{
    public static void ConfigurePgvectorRag(this ModelBuilder builder, bool isNpgsql = true)
    {
        Check.NotNull(builder, nameof(builder));

        if (isNpgsql)
            builder.HasPostgresExtension("vector");

        builder.Entity<DocumentChunk>(b =>
        {
            // 表名沿用主库前缀（Slice D cutover 时不改表名，仅改 owning context + history 表）。
            b.ToTable(PgvectorRagDbProperties.DbTablePrefix + "DocumentChunks", PgvectorRagDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChunkText)
                .IsRequired()
                .HasMaxLength(DocumentChunkConsts.MaxChunkTextLength);

            // 反范式化字段（来自 Document 聚合）。Slice B 已在 chunk 上落地这些字段，
            // provider 检索路径不再 JOIN PaperbaseDocuments——这是本 context 能独立运行（甚至跨 DBMS）的前提。
            b.Property(x => x.DocumentTypeCode)
                .HasMaxLength(DocumentConsts.MaxDocumentTypeCodeLength);
            b.Property(x => x.Title)
                .HasMaxLength(DocumentChunkConsts.MaxTitleLength);

            if (isNpgsql)
            {
                // EmbeddingVector CLR 类型是 float[]；pgvector 列类型保持 vector(N)。
                // HasConversion 负责在写入/读取时转换，CosineDistance 查询仍由 EFCore repository 通过 EF.Property<Vector>() 完成。
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasColumnType($"vector({PgvectorRagDbProperties.EmbeddingVectorDimension})")
                    .HasConversion(
                        v => new Vector(v),
                        v => v.ToArray());
            }
            else
            {
                // SQLite (test): serialize vector as comma-separated floats.
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasConversion(
                        v => string.Join(",", v),
                        s => s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(float.Parse).ToArray());
            }

            // 注意：本 context 不映射 Document 实体——chunk 与 document 之间的 FK CASCADE 关系
            // 在 Slice C 期间仍由主 PaperbaseDbContext 上的同名 mapping 持有（两个 context 共用同一物理表）。
            // Slice E 会引入 DocumentDeletingEventHandler 替代 EF FK CASCADE，跨 context 也安全。

            // (DocumentId, ChunkIndex) 唯一；DocumentId 单列查询命中此索引前缀
            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();

            // (TenantId, DocumentTypeCode) 复合索引：跨文档按文档类型 QA 的主路径
            // （PgvectorDocumentVectorStore 在 vector / keyword 检索前先按这两个字段过滤）。
            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode });
        });
    }
}
