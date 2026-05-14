using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Domain;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(PaperbaseContractsDomainSharedModule)
)]
public class PaperbaseContractsDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<DocumentTypeOptions>(options =>
        {
            options.Register(new DocumentTypeDefinition(
                PaperbaseContractsDocumentTypes.General,
                LocalizableString.Create<PaperbaseContractsResource>("DocumentType:Contract"))
            {
                ConfidenceThreshold = 0.80,
                Priority = 10
            });
        });
    }
}
