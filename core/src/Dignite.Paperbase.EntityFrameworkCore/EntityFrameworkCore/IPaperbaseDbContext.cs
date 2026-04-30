using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Chat;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.EntityFrameworkCore;

[ConnectionStringName(PaperbaseDbProperties.ConnectionStringName)]
public interface IPaperbaseDbContext : IEfCoreDbContext
{
    DbSet<Document> Documents { get; }
    DbSet<DocumentPipelineRun> DocumentPipelineRuns { get; }
    DbSet<DocumentRelation> DocumentRelations { get; }
    DbSet<ChatConversation> ChatConversations { get; }
}
