using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Documents.Pipelines.FieldExtraction;

/// <summary>
/// 订阅 <see cref="DocumentClassifiedEto"/>：分类完成后按当前文档类型的
/// <see cref="DocumentTypeDefinition.Fields"/> 跑 Host 字段抽取，把结果写入
/// <c>Document.SystemFieldsJson</c>，并发布 <see cref="MetadataExtractedEto"/>。
/// <para>
/// 这是 #168 Host 字段抽取流水线的接入点。租户字段（#169 B 机制）由独立 handler 处理，
/// 两者订阅同一 DocumentClassifiedEto，但各自管理自己的存储 + ETO。
/// </para>
/// </summary>
public class HostFieldExtractionEventHandler
    : IDistributedEventHandler<DocumentClassifiedEto>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentTypeOptions _documentTypeOptions;
    private readonly HostFieldExtractionWorkflow _workflow;
    private readonly OutboxEventManager _outboxEventManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<HostFieldExtractionEventHandler> _logger;

    public HostFieldExtractionEventHandler(
        IDocumentRepository documentRepository,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        HostFieldExtractionWorkflow workflow,
        OutboxEventManager outboxEventManager,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<HostFieldExtractionEventHandler> logger)
    {
        _documentRepository = documentRepository;
        _documentTypeOptions = documentTypeOptions.Value;
        _workflow = workflow;
        _outboxEventManager = outboxEventManager;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public async Task HandleEventAsync(DocumentClassifiedEto eventData)
    {
        var typeDef = _documentTypeOptions.Types.FirstOrDefault(t => t.TypeCode == eventData.DocumentTypeCode);
        if (typeDef == null || typeDef.Fields.Count == 0)
        {
            // 该类型没有 Host 字段定义——直接发 MetadataExtractedEto（field count = 0）让下游推进。
            await PublishMetadataExtractedAsync(eventData, fieldCount: 0);
            return;
        }

        // 显式恢复事件携带的租户上下文——分布式事件 handler 在 IIS / Hangfire worker 上下文中
        // ICurrentTenant 不一定自动还原（防 doc-chat-anti-patterns.md 反例 B）。
        using (_currentTenant.Change(eventData.TenantId))
        {
            var extracted = await _workflow.ExtractAsync(typeDef.Fields.ToList(), eventData.Markdown ?? string.Empty);
            var nonNullCount = extracted.Count(kv => kv.Value != null);

            using var uow = _unitOfWorkManager.Begin(requiresNew: true);

            var document = await _documentRepository.FindAsync(eventData.DocumentId, includeDetails: false);
            if (document == null)
            {
                _logger.LogWarning(
                    "DocumentClassifiedEto for missing document {DocumentId} — host-field extraction skipped.",
                    eventData.DocumentId);
                return;
            }

            // 多租户显式断言（防 ambient filter 被 disable 的路径）
            if (document.TenantId != eventData.TenantId)
            {
                _logger.LogWarning(
                    "Cross-tenant DocumentClassifiedEto received: event tenant={EventTenant} document tenant={DocTenant} document={DocId}",
                    eventData.TenantId, document.TenantId, eventData.DocumentId);
                return;
            }

            var json = JsonSerializer.Serialize(extracted);
            document.SetSystemFieldsJson(json);
            await _documentRepository.UpdateAsync(document, autoSave: true);

            await uow.CompleteAsync();

            await PublishMetadataExtractedAsync(eventData, nonNullCount);
        }
    }

    private async Task PublishMetadataExtractedAsync(DocumentClassifiedEto source, int fieldCount)
    {
        await _outboxEventManager.PublishAsync(
            source.TenantId,
            source.DocumentId,
            new MetadataExtractedEto
            {
                DocumentId = source.DocumentId,
                TenantId = source.TenantId,
                DocumentTypeCode = source.DocumentTypeCode,
                ExtractedFieldCount = fieldCount
            });
    }
}
