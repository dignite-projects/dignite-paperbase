using System;
using System.Linq;
using Dignite.Paperbase.Documents;
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
            });

            b.HasMany(x => x.PipelineRuns)
                .WithOne()
                .HasForeignKey(pr => pr.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.LifecycleStatus);
            b.HasIndex(x => x.ReviewStatus);
            b.HasIndex(x => x.DocumentTypeCode);
            b.HasIndex(x => x.CreationTime);
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
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "DocumentChunks", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChunkText)
                .IsRequired()
                .HasMaxLength(DocumentChunkConsts.MaxChunkTextLength);

            if (isNpgsql)
            {
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasColumnType($"vector({PaperbaseDbProperties.EmbeddingVectorDimension})");
            }
            else
            {
                // SQLite: serialize vector as comma-separated floats
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasConversion(
                        v => string.Join(",", v.ToArray()),
                        s => new Vector(s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(float.Parse).ToArray()));
            }

            b.HasOne<Document>()
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // (DocumentId, ChunkIndex) 唯一；DocumentId 单列查询命中此索引前缀
            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
        });
    }
}
