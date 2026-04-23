using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Contracts.Contracts;
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
    private readonly IFieldExtractor _fieldExtractor;
    private readonly ICurrentTenant _currentTenant;

    public ContractDocumentHandler(
        IContractRepository contractRepository,
        ContractManager contractManager,
        IFieldExtractor fieldExtractor,
        ICurrentTenant currentTenant)
    {
        _contractRepository = contractRepository;
        _contractManager = contractManager;
        _fieldExtractor = fieldExtractor;
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
            var extractionResult = await _fieldExtractor.ExtractAsync(new FieldExtractionRequest
            {
                ExtractedText = eventData.ExtractedText ?? string.Empty,
                DocumentTypeCode = eventData.DocumentTypeCode,
                Fields = ContractFieldSchemas.All
            });

            var fields = ExtractedContractFields.FromDictionary(extractionResult.Fields);
            var existing = await _contractRepository.FindByDocumentIdAsync(eventData.DocumentId);

            if (existing != null)
            {
                existing.UpdateExtractedFields(fields);
                await _contractRepository.UpdateAsync(existing, autoSave: true);
            }
            else
            {
                var contract = await _contractManager.CreateAsync(
                    eventData.DocumentId,
                    eventData.DocumentTypeCode,
                    fields);
                await _contractRepository.InsertAsync(contract, autoSave: true);
            }
        }
    }
}
