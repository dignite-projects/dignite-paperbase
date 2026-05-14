using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase;

[DependsOn(
    typeof(PaperbaseDomainModule),
    typeof(PaperbaseTestBaseModule)
)]
public class PaperbaseDomainTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<DocumentTypeOptions>(options =>
        {
            options.Register(new DocumentTypeDefinition(
                "contract.general",
                new FixedLocalizableString("Contract")));
        });
    }
}
