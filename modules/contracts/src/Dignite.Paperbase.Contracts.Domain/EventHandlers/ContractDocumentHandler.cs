using System.Text;
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
    IDistributedEventHandler<DocumentDeletedEto>,
    IDistributedEventHandler<DocumentRestoredEto>,
    ITransientDependency
{
    private readonly IContractRepository _contractRepository;
    private readonly ContractManager _contractManager;
    private readonly IChatClient _chatClient;
    private readonly ICurrentTenant _currentTenant;
    private readonly IContractExtractionExampleProvider _exampleProvider;

    public ContractDocumentHandler(
        IContractRepository contractRepository,
        ContractManager contractManager,
        IChatClient chatClient,
        ICurrentTenant currentTenant,
        IContractExtractionExampleProvider exampleProvider)
    {
        _contractRepository = contractRepository;
        _contractManager = contractManager;
        _chatClient = chatClient;
        _currentTenant = currentTenant;
        _exampleProvider = exampleProvider;
    }

    public virtual async Task HandleEventAsync(DocumentClassifiedEto eventData)
    {
        if (string.IsNullOrEmpty(eventData.DocumentTypeCode) ||
            !eventData.DocumentTypeCode.StartsWith(ContractsDocumentTypes.Prefix))
        {
            return;
        }

        using (_currentTenant.Change(eventData.TenantId))
        {
            // 字段抽取直接吃 Markdown：结构信号（标题/表格/列表）有助于 LLM 准确定位金额、日期等字段。
            var extraction = await ExtractFieldsAsync(
                eventData.Markdown ?? string.Empty,
                eventData.DocumentTypeCode);
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

    public virtual async Task HandleEventAsync(DocumentDeletedEto eventData)
    {
        using (_currentTenant.Change(eventData.TenantId))
        {
            var contract = await _contractRepository.FindByDocumentIdAsync(eventData.DocumentId);
            if (contract == null)
            {
                return;
            }

            contract.ArchiveBecauseDocumentDeleted();
            await _contractRepository.UpdateAsync(contract, autoSave: true);
        }
    }

    public virtual async Task HandleEventAsync(DocumentRestoredEto eventData)
    {
        using (_currentTenant.Change(eventData.TenantId))
        {
            var contract = await _contractRepository.FindByDocumentIdAsync(eventData.DocumentId);
            if (contract == null)
            {
                return;
            }

            contract.RestoreBecauseDocumentRestored();
            await _contractRepository.UpdateAsync(contract, autoSave: true);
        }
    }

    protected virtual async Task<ContractExtractionResult> ExtractFieldsAsync(
        string extractedText,
        string documentTypeCode)
    {
        var instructions = await BuildExtractionInstructionsAsync(documentTypeCode);
        var agent = new ChatClientAgent(
            _chatClient,
            instructions: instructions);

        var run = await agent.RunAsync<ContractExtractionResult>(extractedText);
        return run.Result ?? new ContractExtractionResult();
    }

    protected virtual async Task<string> BuildExtractionInstructionsAsync(string documentTypeCode)
    {
        var examples = await _exampleProvider.GetExamplesAsync(documentTypeCode);
        if (examples.Count == 0)
        {
            return ContractAgentInstructions.SystemPrompt;
        }

        var sb = new StringBuilder(ContractAgentInstructions.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine("以下は人間が修正済みの抽出例です。同じ種類の誤りを避けてください。");

        foreach (var example in examples)
        {
            if (!string.IsNullOrWhiteSpace(example.SourceExcerpt))
            {
                sb.AppendLine("入力抜粋:");
                sb.AppendLine(example.SourceExcerpt);
            }

            sb.AppendLine("修正済み JSON:");
            sb.AppendLine(example.CorrectedJson);
        }

        return sb.ToString();
    }
}
