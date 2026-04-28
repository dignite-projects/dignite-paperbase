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
/// pgvector RAG provider 的实体映射。
/// </summary>
public static class PgvectorRagDbContextModelCreatingExtensions
{
    public static void ConfigurePgvectorRag(this ModelBuilder builder, bool isNpgsql = true)
    {
        Check.NotNull(builder, nameof(builder));

        if (isNpgsql)
            builder.HasPostgresExtension("vector");

        // ── DocumentChunk ─────────────────────────────────────────────────────────
        builder.Entity<DocumentChunk>(b =>
        {
            b.ToTable(PgvectorRagDbProperties.DbTablePrefix + "DocumentChunks", PgvectorRagDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChunkText)
                .IsRequired()
                .HasMaxLength(DocumentChunkConsts.MaxChunkTextLength);

            b.Property(x => x.DocumentTypeCode)
                .HasMaxLength(DocumentConsts.MaxDocumentTypeCodeLength);
            b.Property(x => x.Title)
                .HasMaxLength(DocumentChunkConsts.MaxTitleLength);

            if (isNpgsql)
            {
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasColumnType($"vector({PgvectorRagDbProperties.EmbeddingVectorDimension})")
                    .HasConversion(
                        v => new Vector(v),
                        v => v.ToArray());
            }
            else
            {
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasConversion(
                        v => string.Join(",", v),
                        s => s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(float.Parse).ToArray());
            }

            b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode });
        });

        // ── DocumentVector ────────────────────────────────────────────────────────
        builder.Entity<DocumentVector>(b =>
        {
            b.ToTable(PgvectorRagDbProperties.DbTablePrefix + "DocumentVectors", PgvectorRagDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.DocumentTypeCode)
                .HasMaxLength(DocumentConsts.MaxDocumentTypeCodeLength);

            b.Property(x => x.ChunkCount)
                .IsRequired();

            if (isNpgsql)
            {
                // Same dimension as chunks — mean-pooled from the same embedding model.
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasColumnType($"vector({PgvectorRagDbProperties.EmbeddingVectorDimension})")
                    .HasConversion(
                        v => new Vector(v),
                        v => v.ToArray());
            }
            else
            {
                b.Property(x => x.EmbeddingVector)
                    .IsRequired()
                    .HasConversion(
                        v => string.Join(",", v),
                        s => s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(float.Parse).ToArray());
            }

            // Cross-document similarity queries filter by tenant + type.
            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode });
        });
    }
}
