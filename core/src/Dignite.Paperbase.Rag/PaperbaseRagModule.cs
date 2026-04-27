using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag;

[DependsOn(typeof(AbpMultiTenancyModule))]
public class PaperbaseRagModule : AbpModule
{
}
