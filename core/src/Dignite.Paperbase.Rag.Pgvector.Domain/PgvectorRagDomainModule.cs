using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Rag.Pgvector;

[DependsOn(
    typeof(PgvectorRagDomainSharedModule),
    typeof(PaperbaseRagModule),
    typeof(AbpDddDomainModule),
    typeof(PaperbaseDomainModule)
)]
public class PgvectorRagDomainModule : AbpModule
{
}
