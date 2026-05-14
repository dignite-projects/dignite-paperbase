using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Domain;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Contracts;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(ContractsDomainSharedModule)
)]
public class ContractsDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<DocumentTypeOptions>(options =>
        {
            options.Register(new DocumentTypeDefinition(
                ContractsDocumentTypes.General,
                LocalizableString.Create<ContractsResource>("DocumentType:Contract"))
            {
                ConfidenceThreshold = 0.80,
                Priority = 10
            });
        });
    }
}
