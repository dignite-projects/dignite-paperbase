using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Dignite.Paperbase.Documents;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Contracts.EventHandlers;

public class ContractDocumentHandler :
    IDistributedEventHandler<DocumentClassifiedEto>,
    ITransientDependency
{
    private readonly IContractRepository _contractRepository;
    private readonly ContractManager _contractManager;
    private readonly ICurrentTenant _currentTenant;

    public ContractDocumentHandler(
        IContractRepository contractRepository,
        ContractManager contractManager,
        ICurrentTenant currentTenant)
    {
        _contractRepository = contractRepository;
        _contractManager = contractManager;
        _currentTenant = currentTenant;
    }

    public virtual async Task HandleEventAsync(DocumentClassifiedEto eventData)
    {
        if (!eventData.DocumentTypeCode.StartsWith("contract."))
        {
            return;
        }

        using (_currentTenant.Change(eventData.TenantId))
        {
            var fields = RegexContractExtractor.Extract(eventData.ExtractedText ?? string.Empty);
            var existing = await _contractRepository.FindByDocumentIdAsync(eventData.DocumentId);

            if (existing != null)
            {
                existing.UpdateExtractedFields(fields);
                await _contractRepository.UpdateAsync(existing, autoSave: true);
                return;
            }

            var contract = await _contractManager.CreateAsync(
                eventData.DocumentId,
                eventData.DocumentTypeCode,
                fields);

            await _contractRepository.InsertAsync(contract, autoSave: true);
        }
    }
}
