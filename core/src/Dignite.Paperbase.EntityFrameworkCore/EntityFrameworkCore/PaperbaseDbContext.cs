using Dignite.Paperbase.Documents;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.EntityFrameworkCore;

[ConnectionStringName(PaperbaseDbProperties.ConnectionStringName)]
public class PaperbaseDbContext : AbpDbContext<PaperbaseDbContext>, IPaperbaseDbContext
{
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentPipelineRun> DocumentPipelineRuns { get; set; }
    public DbSet<OutboxEvent> OutboxEvents { get; set; }
    public DbSet<TenantFieldDefinition> TenantFieldDefinitions { get; set; }
    public DbSet<DocumentTenantField> DocumentTenantFields { get; set; }

    public PaperbaseDbContext(DbContextOptions<PaperbaseDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigurePaperbase();
    }
}
