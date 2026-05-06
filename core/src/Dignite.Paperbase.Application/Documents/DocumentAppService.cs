using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Pipelines.Classification;
using Dignite.Paperbase.Documents.Pipelines.Embedding;
using Dignite.Paperbase.Documents.Pipelines.TextExtraction;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Dignite.Paperbase.Documents.Events;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EventBus.Local;

namespace Dignite.Paperbase.Documents;

public class DocumentAppService : PaperbaseAppService, IDocumentAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly ILocalEventBus _localEventBus;

    public DocumentAppService(
        IDocumentRepository documentRepository,
        IDocumentRelationRepository relationRepository,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IBackgroundJobManager backgroundJobManager,
        DocumentPipelineRunManager pipelineRunManager,
        IDistributedEventBus distributedEventBus,
        ILocalEventBus localEventBus)
    {
        _documentRepository = documentRepository;
        _relationRepository = relationRepository;
        _blobContainer = blobContainer;
        _backgroundJobManager = backgroundJobManager;
        _pipelineRunManager = pipelineRunManager;
        _distributedEventBus = distributedEventBus;
        _localEventBus = localEventBus;
    }

    public virtual async Task<DocumentDto> GetAsync(Guid id)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);
        var document = await _documentRepository.GetAsync(id);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    public virtual async Task<PagedResultDto<DocumentListItemDto>> GetListAsync(GetDocumentListInput input)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);

        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);

        var totalCount = await AsyncExecuter.CountAsync(query);

        query = ApplySorting(query, input.Sorting);
        query = query.Skip(input.SkipCount).Take(input.MaxResultCount);

        var documents = await AsyncExecuter.ToListAsync(query);

        return new PagedResultDto<DocumentListItemDto>(
            totalCount,
            ObjectMapper.Map<List<Document>, List<DocumentListItemDto>>(documents));
    }

    [Authorize(PaperbasePermissions.Documents.Upload)]
    public virtual async Task<DocumentDto> UploadAsync(UploadDocumentInput input)
    {
        var fileName = input.File.FileName ?? "document";
        var contentType = input.File.ContentType ?? "application/octet-stream";
        var extension = Path.GetExtension(fileName);

        await using var source = input.File.GetStream();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var fileSize = bytes.LongLength;

        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var existing = await _documentRepository.FindByContentHashAsync(contentHash);
        if (existing != null)
        {
            var errorCode = existing.IsDeleted
                ? PaperbaseErrorCodes.DocumentInRecycleBin
                : PaperbaseErrorCodes.DocumentDuplicate;

            throw new BusinessException(errorCode)
                .WithData("FileName", fileName)
                .WithData("ExistingDocumentId", existing.Id);
        }

        var blobName = GuidGenerator.Create().ToString("N") + extension;
        using (var saveStream = new MemoryStream(bytes, writable: false))
        {
            await _blobContainer.SaveAsync(blobName, saveStream);
        }

        var sourceType = SourceType.Physical; // placeholder；提取完成后由 BackgroundJob 回写实际值
        var fileOrigin = new FileOrigin(
            CurrentUser.UserName ?? string.Empty,
            contentType,
            contentHash,
            fileSize,
            originalFileName: fileName);

        var document = new Document(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            blobName,
            sourceType,
            fileOrigin);

        await _documentRepository.InsertAsync(document, autoSave: true);

        await _backgroundJobManager.EnqueueAsync(
            new DocumentTextExtractionJobArgs { DocumentId = document.Id });

        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    public virtual async Task<IRemoteStreamContent> GetBlobAsync(Guid id)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);

        var document = await _documentRepository.GetAsync(id);
        var stream = await _blobContainer.GetAsync(document.OriginalFileBlobName);

        return new RemoteStreamContent(
            stream,
            document.FileOrigin.OriginalFileName,
            document.FileOrigin.ContentType,
            disposeStream: true);
    }

    [Authorize(PaperbasePermissions.Documents.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var document = await _documentRepository.GetAsync(id);

        // 立即投递本地事件（onUnitOfWorkComplete:false），handler 在主 UoW 仍活跃时
        // 注册 after-commit 回调，主事务提交后再以新 UoW 清理各 provider 的 chunks。
        await _localEventBus.PublishAsync(
            new DocumentDeletingEvent(id, document.TenantId),
            onUnitOfWorkComplete: false);

        await _documentRepository.DeleteAsync(id);

        var relations = await _relationRepository.GetListByDocumentIdAsync(id);
        if (relations.Count > 0)
        {
            await _relationRepository.DeleteManyAsync(relations);
        }

        await _distributedEventBus.PublishAsync(new DocumentDeletedEto
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            DocumentTypeCode = document.DocumentTypeCode
        });
    }

    [Authorize(PaperbasePermissions.Documents.Restore)]
    public virtual async Task RestoreAsync(Guid id)
    {
        using (DataFilter.Disable<ISoftDelete>())
        {
            var document = await _documentRepository.GetAsync(id);
            if (!document.IsDeleted)
            {
                return;
            }

            document.IsDeleted = false;
            document.DeletionTime = null;
            document.DeleterId = null;

            await _documentRepository.UpdateAsync(document);

            await _distributedEventBus.PublishAsync(new DocumentRestoredEto
            {
                DocumentId = document.Id,
                TenantId = document.TenantId,
                DocumentTypeCode = document.DocumentTypeCode
            });
        }
    }

    [Authorize(PaperbasePermissions.Documents.Export)]
    public virtual async Task<IRemoteStreamContent> GetExportAsync(GetDocumentListInput input)
    {
        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);
        query = ApplySorting(query, input.Sorting);

        var documents = await AsyncExecuter.ToListAsync(query);
        var csv = BuildDocumentCsv(documents);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return new RemoteStreamContent(new MemoryStream(bytes), "documents.csv", "text/csv");
    }

    /// <summary>
    /// 重试单条 pipeline。当前仅 <see cref="PipelineRunStatus.Failed"/> 可重试；
    /// Pending/Running 抛 <c>PipelineRetryInProgress</c>，Succeeded/Skipped 抛 <c>PipelineNotRetryable</c>。
    /// 重试只入队对应的 BackgroundJob，新的 <c>DocumentPipelineRun</c> 由 Job 的 <c>StartAsync</c> 创建——
    /// 避免 AppService 与 Job 各创建一条 Pending Run 的竞态。
    /// 链式重放语义（隐式）：重试 <c>text-extraction</c> → 成功后链触发 <c>classification</c> → <c>embedding</c>；
    /// 重试 <c>classification</c> → 链触发 <c>embedding</c>；重试 <c>embedding</c> 是叶子。
    /// </summary>
    [Authorize(PaperbasePermissions.Documents.Pipelines.Retry)]
    public virtual async Task RetryPipelineAsync(Guid id, RetryPipelineInput input)
    {
        if (!PaperbasePipelines.RetryablePipelines.Contains(input.PipelineCode))
        {
            throw new BusinessException(PaperbaseErrorCodes.UnknownPipelineCode)
                .WithData("PipelineCode", input.PipelineCode);
        }

        var document = await _documentRepository.GetAsync(id);

        // 显式租户断言 + 软删除门：不依赖 ambient DataFilter。
        // 反例参考 .claude/rules/doc-chat-anti-patterns.md 反例 B。
        if (document.TenantId != CurrentTenant.Id)
        {
            Logger.LogWarning(
                "RetryPipelineAsync tenant mismatch: doc={DocumentId} docTenant={DocTenantId} currentTenant={CurrentTenantId}",
                document.Id, document.TenantId, CurrentTenant.Id);
            throw new EntityNotFoundException(typeof(Document), id);
        }

        if (document.IsDeleted)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentInRecycleBin)
                .WithData("FileName", document.OriginalFileBlobName);
        }

        var latestRun = document.GetLatestRun(input.PipelineCode);
        if (latestRun == null)
        {
            throw new BusinessException(PaperbaseErrorCodes.PipelineNeverRan)
                .WithData("PipelineCode", input.PipelineCode);
        }

        switch (latestRun.Status)
        {
            case PipelineRunStatus.Pending:
            case PipelineRunStatus.Running:
                throw new BusinessException(PaperbaseErrorCodes.PipelineRetryInProgress)
                    .WithData("PipelineCode", input.PipelineCode);
            case PipelineRunStatus.Succeeded:
            case PipelineRunStatus.Skipped:
                throw new BusinessException(PaperbaseErrorCodes.PipelineNotRetryable)
                    .WithData("PipelineCode", input.PipelineCode)
                    .WithData("Status", latestRun.Status.ToString());
        }

        Logger.LogInformation(
            "RetryPipelineAsync user={UserId} tenant={TenantId} doc={DocumentId} pipeline={PipelineCode} previousAttempt={Attempt}",
            CurrentUser.Id, CurrentTenant.Id, document.Id, input.PipelineCode, latestRun.AttemptNumber);

        await EnqueuePipelineJobAsync(document.Id, input.PipelineCode);
    }

    /// <summary>
    /// 按 PipelineCode 入队对应 BackgroundJob。
    /// 不在 AppService 里调用 <see cref="DocumentPipelineRunManager.StartAsync"/>——
    /// 新 Run 由 BackgroundJob 自身按"原始链路一致"的方式创建，避免出现两条 Pending Run。
    /// </summary>
    protected virtual Task EnqueuePipelineJobAsync(Guid documentId, string pipelineCode)
    {
        return pipelineCode switch
        {
            PaperbasePipelines.TextExtraction => _backgroundJobManager.EnqueueAsync(
                new DocumentTextExtractionJobArgs { DocumentId = documentId }),
            PaperbasePipelines.Classification => _backgroundJobManager.EnqueueAsync(
                new DocumentClassificationJobArgs { DocumentId = documentId }),
            PaperbasePipelines.Embedding => _backgroundJobManager.EnqueueAsync(
                new DocumentEmbeddingJobArgs { DocumentId = documentId }),
            _ => throw new BusinessException(PaperbaseErrorCodes.UnknownPipelineCode)
                .WithData("PipelineCode", pipelineCode)
        };
    }

    [Authorize(PaperbasePermissions.Documents.ConfirmClassification)]
    public virtual async Task<DocumentDto> ConfirmClassificationAsync(Guid id, ConfirmClassificationInput input)
    {
        var document = await _documentRepository.GetAsync(id);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);

        await _pipelineRunManager.CompleteManualClassificationAsync(document, run, input.DocumentTypeCode);

        await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            DocumentTypeCode = input.DocumentTypeCode,
            ClassificationConfidence = 1.0,
            Markdown = document.Markdown
        });

        await _backgroundJobManager.EnqueueAsync(
            new DocumentEmbeddingJobArgs { DocumentId = document.Id });

        await _documentRepository.UpdateAsync(document);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    protected virtual IQueryable<Document> ApplyFilter(IQueryable<Document> query, GetDocumentListInput input)
    {
        if (input.LifecycleStatus.HasValue)
            query = query.Where(x => x.LifecycleStatus == input.LifecycleStatus.Value);

        if (!input.DocumentTypeCode.IsNullOrWhiteSpace())
            query = query.Where(x => x.DocumentTypeCode == input.DocumentTypeCode);

        if (input.ReviewStatus.HasValue)
            query = query.Where(d => d.ReviewStatus == input.ReviewStatus.Value);

        return query;
    }

    protected virtual IQueryable<Document> ApplySorting(IQueryable<Document> query, string? sorting)
    {
        return sorting switch
        {
            "creationTime" => query.OrderBy(x => x.CreationTime),
            _ => query.OrderByDescending(x => x.CreationTime)
        };
    }

    private static string BuildDocumentCsv(List<Document> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,DocumentTypeCode,LifecycleStatus,OriginalFileName,ContentType,CreationTime");

        foreach (var d in documents)
        {
            sb.AppendLine(string.Join(",",
                d.Id,
                EscapeCsv(d.DocumentTypeCode),
                d.LifecycleStatus.ToString(),
                EscapeCsv(d.FileOrigin.OriginalFileName),
                EscapeCsv(d.FileOrigin.ContentType),
                d.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (value.IsNullOrEmpty()) return string.Empty;
        if (value!.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
