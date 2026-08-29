using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Vault.Extract.EntityFrameworkCore;

[ConnectionStringName(VaultExtractDbProperties.ConnectionStringName)]
public class VaultExtractDbContext : AbpDbContext<VaultExtractDbContext>, IVaultExtractDbContext
{
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentPipelineRun> DocumentPipelineRuns { get; set; }
    public DbSet<DocumentSegment> DocumentSegments { get; set; }
    public DbSet<DocumentType> DocumentTypes { get; set; }
    public DbSet<FieldDefinition> FieldDefinitions { get; set; }
    // Field architecture v3 (#558), additive alongside FieldDefinition until #561's migration runs.
    public DbSet<Field> Fields { get; set; }
    public DbSet<DocumentFlexFieldIndex> DocumentFlexFieldIndexes { get; set; }
    public DbSet<Cabinet> Cabinets { get; set; }

    public VaultExtractDbContext(DbContextOptions<VaultExtractDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureVaultExtract();
    }
}
