using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp.Domain;
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
            options.Register(new DocumentTypeDefinition(ContractsDocumentTypes.General, "Contract")
            {
                MatchKeywords =
                {
                    "契約書",
                    "合意書",
                    "甲",
                    "乙",
                    "契約期間",
                    "署名"
                },
                ConfidenceThreshold = 0.80,
                Priority = 10
            });
        });
    }
}
