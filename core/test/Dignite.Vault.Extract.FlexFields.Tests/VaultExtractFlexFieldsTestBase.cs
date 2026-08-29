using Volo.Abp;
using Volo.Abp.Testing;

namespace Dignite.Vault.Extract.FlexFields;

public abstract class VaultExtractFlexFieldsTestBase : AbpIntegratedTest<VaultExtractFlexFieldsTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }
}
