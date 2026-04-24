using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
    private readonly IChatClient _chatClient;
    private readonly ICurrentTenant _currentTenant;

    public ContractDocumentHandler(
        IContractRepository contractRepository,
        ContractManager contractManager,
        IChatClient chatClient,
        ICurrentTenant currentTenant)
    {
        _contractRepository = contractRepository;
        _contractManager = contractManager;
        _chatClient = chatClient;
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
            var extraction = await ExtractFieldsAsync(eventData.ExtractedText ?? string.Empty);
            var fields = ExtractedContractFields.FromAgentResult(extraction);

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

    protected virtual async Task<ContractExtractionResult> ExtractFieldsAsync(string extractedText)
    {
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: ContractAgentInstructions.SystemPrompt);

        var run = await agent.RunAsync<ContractExtractionResult>(extractedText);
        return run.Result ?? new ContractExtractionResult();
    }
}
