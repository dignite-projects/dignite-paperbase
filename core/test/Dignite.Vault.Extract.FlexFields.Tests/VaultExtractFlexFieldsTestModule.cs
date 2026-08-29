using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// Depends on <see cref="VaultExtractFlexFieldsModule"/> only - no Domain, no EF Core, no host. That is
/// the layering claim under test as much as anything else: Vault Extract's own field types need nothing
/// but the FlexFields kernel's Abstractions to resolve, validate and localize.
/// </summary>
[DependsOn(
    typeof(AbpTestBaseModule),
    typeof(AbpAutofacModule),
    typeof(VaultExtractFlexFieldsModule)
    )]
public class VaultExtractFlexFieldsTestModule : AbpModule
{
}
