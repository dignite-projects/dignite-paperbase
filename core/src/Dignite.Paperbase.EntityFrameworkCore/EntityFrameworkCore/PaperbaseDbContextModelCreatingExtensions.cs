using System;
using System.Linq;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dignite.Paperbase.EntityFrameworkCore;

public static class PaperbaseDbContextModelCreatingExtensions
{
    public static void ConfigurePaperbase(this ModelBuilder builder, bool isNpgsql = true)
    {
        Check.NotNull(builder, nameof(builder));

        if (isNpgsql)
            builder.HasPostgresExtension("vector");

        builder.Entity<Document>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "Documents", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.OriginalFileBlobName).IsRequired().HasMaxLength(DocumentConsts.MaxOriginalFileBlobNameLength);
            b.Property(x => x.SourceType).IsRequired();
            b.Property(x => x.DocumentTypeCode).HasMaxLength(DocumentConsts.MaxDocumentTypeCodeLength);
            b.Property(x => x.LifecycleStatus).IsRequired();
            b.Property(x => x.ReviewStatus).IsRequired();
            b.Property(x => x.ClassificationReason).HasColumnType("text");
            b.Property(x => x.ExtractedText).HasColumnType("text");

            b.OwnsOne(x => x.FileOrigin, fo =>
            {
                fo.Property(x => x.UploadedByUserName)
                    .IsRequired()
                    .HasMaxLength(FileOriginConsts.MaxUploadedByUserNameLength);
                fo.Property(x => x.OriginalFileName).HasMaxLength(FileOriginConsts.MaxOriginalFileNameLength);
                fo.Property(x => x.ContentType)
                    .IsRequired()
                    .HasMaxLength(FileOriginConsts.MaxContentTypeLength);
                fo.Property(x => x.ContentHash)
                    .IsRequired()
                    .HasMaxLength(FileOriginConsts.MaxContentHashLength);
            });

            b.HasMany(x => x.PipelineRuns)
                .WithOne()
                .HasForeignKey(pr => pr.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.LifecycleStatus);
            b.HasIndex(x => x.ReviewStatus);
            b.HasIndex(x => x.DocumentTypeCode);
            b.HasIndex(x => x.CreationTime);

            // 每租户范围内按文件字节级 SHA-256 唯一；NULLS NOT DISTINCT 让单租户场景下 (NULL, hash) 也能正确判重。
            // 跨 owned-entity 索引 EF Core 不直接支持，由迁移文件用 raw SQL 创建唯一索引。
        });

        builder.Entity<DocumentPipelineRun>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "DocumentPipelineRuns", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.PipelineCode).IsRequired().HasMaxLength(DocumentPipelineRunConsts.MaxPipelineCodeLength);
            b.Property(x => x.StatusMessage).HasMaxLength(DocumentPipelineRunConsts.MaxStatusMessageLength);

            // 联合索引：(DocumentId, PipelineCode, AttemptNumber DESC)
            b.HasIndex(x => new { x.DocumentId, x.PipelineCode, x.AttemptNumber });
        });

        builder.Entity<DocumentRelation>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "DocumentRelations", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Description).IsRequired().HasMaxLength(DocumentRelationConsts.MaxDescriptionLength);
            b.Property(x => x.Source).IsRequired();

            b.HasIndex(x => x.SourceDocumentId);
            b.HasIndex(x => x.TargetDocumentId);
        });

        builder.Entity<DocumentChunk>(b =>
        {
            // Slice C：chunk 表的 EF 迁移所有权已转移到独立的 PgvectorRagDbContext。
            // 主 PaperbaseDbContext 仍保留 mapping（避免读旧数据时 navigation 等失效），
            // 但 ExcludeFromMigrations 防止 host migration diff（PaperbaseHostDbContext 走的是
            // 这条路径）和 RelationalDatabaseCreator.CreateTables 重复创建表 / 索引。
            // Slice D cutover 后会从主 context 彻底移除该 mapping。
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "DocumentChunks", PaperbaseDbProperties.DbSchema, t => t.ExcludeFromMigrations());
            b.ConfigureByConvention();

            b.Property(x => x.ChunkText)
                .IsRequired()
                .HasMaxLength(DocumentChunkConsts.MaxChunkTextLength);

            // 反范式化字段（来自 Document 聚合）。让 provider 检索路径不再 JOIN Documents 表，
            // 为 Slice C 切独立 PgvectorRagDbContext 做物理前置——跨 DbContext / 跨 DBMS 不能 JOIN。
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
                    .HasColumnType($"vector({PaperbaseDbProperties.EmbeddingVectorDimension})")
                    .HasConversion(
                        v => new Vector(v),
                        v => v.ToArray());
            }
            else
            {
                // SQLite: serialize vector as comma-separated floats
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasConversion(
                        v => string.Join(",", v),
                        s => s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(float.Parse).ToArray());
            }

            b.HasOne<Document>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // (DocumentId, ChunkIndex) 唯一；DocumentId 单列查询命中此索引前缀
            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();

            // (TenantId, DocumentTypeCode) 复合索引：跨文档按文档类型 QA 的主路径
            // （PgvectorDocumentVectorStore 在 vector / keyword 检索前先按这两个字段过滤）。
            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode });
        });
    }
}
