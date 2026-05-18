using Dignite.Paperbase.Documents;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dignite.Paperbase.EntityFrameworkCore;

public static class PaperbaseDbContextModelCreatingExtensions
{
    public static void ConfigurePaperbase(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Document>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "Documents", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.OriginalFileBlobName).IsRequired().HasMaxLength(DocumentConsts.MaxOriginalFileBlobNameLength);
            b.Property(x => x.SourceType).IsRequired();
            b.Property(x => x.DocumentTypeCode).HasMaxLength(DocumentConsts.MaxDocumentTypeCodeLength);
            b.Property(x => x.LifecycleStatus).IsRequired();
            b.Property(x => x.ReviewStatus).IsRequired();
            b.Property(x => x.ClassificationReason);
            b.Property(x => x.Markdown);
            b.Property(x => x.SystemFieldsJson);
            b.Property(x => x.Title).HasMaxLength(DocumentConsts.MaxTitleLength);

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

        builder.Entity<OutboxEvent>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "OutboxEvents", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EventType).IsRequired().HasMaxLength(OutboxEventConsts.MaxEventTypeLength);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.ScheduledAt).IsRequired();

            // 去重 key：(TenantId, DocumentId, EventType)。OutboxEventManager 通过此索引快速查找现有记录。
            // 唯一索引 + 软删除过滤：同一 key 只允许一条 live 记录（语义去重的存储基础）。
            //
            // <strong>已知限制 — host (single-tenant) deployment</strong>：
            // SQL standard 下 NULL 不等于 NULL，所以两条 (TenantId=NULL, DocumentId=X, EventType=Y)
            // 的行都能 insert 成功。host 单租户场景下 DB 级唯一不强制；多租户部署（每条都有非空 TenantId）
            // 完全受保护。应用层 OutboxEventManager.PublishAsync 内的 FindByKeyAsync 是单租户的兜底
            // 保护，覆盖串行场景；罕见的并发竞争由 ABP 的 DbUpdateException → 上游重试覆盖。
            b.HasIndex(x => new { x.TenantId, x.DocumentId, x.EventType })
                .IsUnique()
                .HasFilter("IsDeleted = 0");

            // 二级索引：扫描 InFlight 事件供后台 worker 检查/重发（如果以后需要）。
            b.HasIndex(x => new { x.Status, x.ScheduledAt });
        });

        builder.Entity<TenantFieldDefinition>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "TenantFieldDefinitions", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.DocumentTypeCode).IsRequired().HasMaxLength(TenantFieldConsts.MaxDocumentTypeCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(TenantFieldConsts.MaxNameLength);
            b.Property(x => x.Prompt).IsRequired().HasMaxLength(TenantFieldConsts.MaxPromptLength);
            b.Property(x => x.DataType).IsRequired();

            // 唯一索引：每租户每类型下字段名唯一。软删除过滤；NULL-tenant 限制同 OutboxEvent。
            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode, x.Name })
                .IsUnique()
                .HasFilter("IsDeleted = 0");

            b.HasIndex(x => new { x.TenantId, x.DocumentTypeCode });
        });

        builder.Entity<DocumentTenantField>(b =>
        {
            b.ToTable(PaperbaseDbProperties.DbTablePrefix + "DocumentTenantFields", PaperbaseDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.FieldName).IsRequired().HasMaxLength(TenantFieldConsts.MaxNameLength);
            b.Property(x => x.Value).HasMaxLength(TenantFieldConsts.MaxValueLength);

            // 唯一索引：每文档每字段名一行
            b.HasIndex(x => new { x.TenantId, x.DocumentId, x.FieldName })
                .IsUnique()
                .HasFilter("IsDeleted = 0");

            // keyword 搜索常用维度：值 + 字段名
            b.HasIndex(x => new { x.TenantId, x.FieldName });
        });
    }
}
