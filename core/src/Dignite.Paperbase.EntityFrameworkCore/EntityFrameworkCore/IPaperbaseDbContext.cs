using Dignite.Paperbase.Documents;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.EntityFrameworkCore;

[ConnectionStringName(PaperbaseDbProperties.ConnectionStringName)]
public interface IPaperbaseDbContext : IEfCoreDbContext
{
    DbSet<Document> Documents { get; }
    DbSet<DocumentPipelineRun> DocumentPipelineRuns { get; }
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<TenantFieldDefinition> TenantFieldDefinitions { get; }
    DbSet<DocumentTenantField> DocumentTenantFields { get; }
}
