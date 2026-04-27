using Dignite.Paperbase.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Rag.Pgvector;

/// <summary>
/// Registers the pgvector-backed <see cref="IDocumentVectorStore"/> implementation.
/// Depends on the Rag abstraction layer and the EF Core + pgvector data layer.
/// </summary>
[DependsOn(
    typeof(PaperbaseRagModule),
    typeof(PaperbaseEntityFrameworkCoreModule))]
public class PgvectorRagModule : AbpModule
{
}
