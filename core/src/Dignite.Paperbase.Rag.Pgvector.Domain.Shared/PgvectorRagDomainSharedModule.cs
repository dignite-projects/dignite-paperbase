using Volo.Abp.Modularity;
using Volo.Abp.Validation;

namespace Dignite.Paperbase.Rag.Pgvector;

[DependsOn(
    typeof(AbpValidationModule)
)]
public class PgvectorRagDomainSharedModule : AbpModule
{
}
