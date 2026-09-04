using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Vault.Extract.EntityFrameworkCore;

[ConnectionStringName(VaultExtractDbProperties.ConnectionStringName)]
public interface IVaultExtractDbContext : IEfCoreDbContext
{
    DbSet<Document> Documents { get; }
    DbSet<DocumentPipelineRun> DocumentPipelineRuns { get; }
    DbSet<DocumentSegment> DocumentSegments { get; }
    DbSet<DocumentType> DocumentTypes { get; }
    DbSet<Field> Fields { get; }
    DbSet<DocumentFlexFieldIndex> DocumentFlexFieldIndexes { get; }
    DbSet<Cabinet> Cabinets { get; }
}
