using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.FieldExtraction;

/// <summary>
/// 订阅 <see cref="DocumentClassifiedEto"/>：分类完成后按租户为该文档类型注册的
/// <see cref="TenantFieldDefinition"/> 列表跑 LLM 抽取，结果落 <see cref="DocumentTenantField"/>，
/// 并发布 <see cref="CustomFieldsExtractedEto"/>。
/// <para>
/// 与 <c>HostFieldExtractionEventHandler</c>（#168）正交：两者订阅同一 ETO，各管自己存储。
/// 共享 LLM 抽取引擎 <see cref="HostFieldExtractionWorkflow"/>（命名上保留 Host，但实际通用：
/// 把 TenantFieldDefinition 转成 HostFieldDefinition 喂进 workflow 即可）。
/// </para>
/// <para>
/// 安全约束（CLAUDE.md）：
/// <list type="bullet">
///   <item>事件携带的 TenantId 显式设入 CurrentTenant，避免 ambient 漏失</item>
///   <item>查询 TenantFieldDefinition 走显式 TenantId 谓词，不依赖 ambient DataFilter</item>
///   <item>租户 Prompt 是用户控制文本，由共享的 <see cref="HostFieldExtractionWorkflow"/>
///         在构建 system prompt 时统一经 <c>PromptBoundary.WrapField</c> 包裹</item>
/// </list>
/// </para>
/// </summary>
public class TenantFieldExtractionEventHandler
    : IDistributedEventHandler<DocumentClassifiedEto>, ITransientDependency
{
    private readonly ITenantFieldDefinitionRepository _definitionRepository;
    private readonly IDocumentTenantFieldRepository _fieldRepository;
    private readonly HostFieldExtractionWorkflow _workflow;
    private readonly OutboxEventManager _outboxEventManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<TenantFieldExtractionEventHandler> _logger;

    public TenantFieldExtractionEventHandler(
        ITenantFieldDefinitionRepository definitionRepository,
        IDocumentTenantFieldRepository fieldRepository,
        HostFieldExtractionWorkflow workflow,
        OutboxEventManager outboxEventManager,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        ILogger<TenantFieldExtractionEventHandler> logger)
    {
        _definitionRepository = definitionRepository;
        _fieldRepository = fieldRepository;
        _workflow = workflow;
        _outboxEventManager = outboxEventManager;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    public async Task HandleEventAsync(DocumentClassifiedEto eventData)
    {
        if (string.IsNullOrWhiteSpace(eventData.DocumentTypeCode))
        {
            return;
        }

        using (_currentTenant.Change(eventData.TenantId))
        {
            var definitions = await _definitionRepository.GetByDocumentTypeAsync(
                eventData.TenantId, eventData.DocumentTypeCode);

            if (definitions.Count == 0)
            {
                // 无租户字段定义——直接发 CustomFieldsExtractedEto（fieldCount=0）让下游 DocumentReady 推进。
                await _outboxEventManager.PublishAsync(
                    eventData.TenantId,
                    eventData.DocumentId,
                    new CustomFieldsExtractedEto
                    {
                        DocumentId = eventData.DocumentId,
                        TenantId = eventData.TenantId,
                        DocumentTypeCode = eventData.DocumentTypeCode,
                        FieldCount = 0
                    });
                return;
            }

            // 转换 TenantFieldDefinition 到 HostFieldDefinition（workflow 接受的 schema 类型）
            var fields = definitions.Select(d => new HostFieldDefinition(
                d.Name, d.Prompt, d.DataType, d.IsRequired)).ToList();

            var extracted = await _workflow.ExtractAsync(fields, eventData.Markdown ?? string.Empty);

            using var uow = _unitOfWorkManager.Begin(requiresNew: true);

            foreach (var (name, value) in extracted)
            {
                var json = value == null ? null : JsonSerializer.Serialize(value);
                var existing = await _fieldRepository.FindByDocumentAndNameAsync(
                    eventData.TenantId, eventData.DocumentId, name);

                if (existing == null)
                {
                    var entity = new DocumentTenantField(
                        _guidGenerator.Create(),
                        eventData.TenantId,
                        eventData.DocumentId,
                        name,
                        json);
                    await _fieldRepository.InsertAsync(entity);
                }
                else
                {
                    existing.UpdateValue(json, null);
                    await _fieldRepository.UpdateAsync(existing);
                }
            }
            await uow.CompleteAsync();

            var nonNullCount = extracted.Count(kv => kv.Value != null);
            await _outboxEventManager.PublishAsync(
                eventData.TenantId,
                eventData.DocumentId,
                new CustomFieldsExtractedEto
                {
                    DocumentId = eventData.DocumentId,
                    TenantId = eventData.TenantId,
                    DocumentTypeCode = eventData.DocumentTypeCode,
                    FieldCount = nonNullCount
                });

            _logger.LogInformation(
                "Tenant field extraction for document {DocumentId} produced {NonNullCount}/{TotalCount} values.",
                eventData.DocumentId, nonNullCount, definitions.Count);
        }
    }
}
