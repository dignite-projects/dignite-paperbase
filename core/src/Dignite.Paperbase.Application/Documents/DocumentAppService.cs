using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Paperbase.Application.Documents;

public class DocumentAppService : PaperbaseAppService, IDocumentAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly SourceTypeDetector _sourceTypeDetector;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IDistributedEventBus _distributedEventBus;

    public DocumentAppService(
        IDocumentRepository documentRepository,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IBackgroundJobManager backgroundJobManager,
        SourceTypeDetector sourceTypeDetector,
        DocumentPipelineRunManager pipelineRunManager,
        IDistributedEventBus distributedEventBus)
    {
        _documentRepository = documentRepository;
        _blobContainer = blobContainer;
        _backgroundJobManager = backgroundJobManager;
        _sourceTypeDetector = sourceTypeDetector;
        _pipelineRunManager = pipelineRunManager;
        _distributedEventBus = distributedEventBus;
    }

    public virtual async Task<DocumentDto> GetAsync(Guid id)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);
        var document = await _documentRepository.GetAsync(id);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    public virtual async Task<PagedResultDto<DocumentDto>> GetListAsync(GetDocumentListInput input)
    {
        await CheckPolicyAsync(PaperbasePermissions.Documents.Default);

        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);

        var totalCount = await AsyncExecuter.CountAsync(query);

        query = ApplySorting(query, input.Sorting);
        query = query.Skip(input.SkipCount).Take(input.MaxResultCount);

        var documents = await AsyncExecuter.ToListAsync(query);

        return new PagedResultDto<DocumentDto>(
            totalCount,
            ObjectMapper.Map<List<Document>, List<DocumentDto>>(documents));
    }

    [Authorize(PaperbasePermissions.Documents.Upload)]
    public virtual async Task<DocumentDto> UploadAsync(UploadDocumentInput input)
    {
        var fileName = input.FileName ?? input.File.FileName ?? "document";
        var contentType = input.File.ContentType ?? "application/octet-stream";
        var fileSize = input.File.ContentLength ?? 0;
        var extension = Path.GetExtension(fileName);
        var blobName = GuidGenerator.Create().ToString("N") + extension;

        await using var stream = input.File.GetStream();
        await _blobContainer.SaveAsync(blobName, stream);

        var sourceType = _sourceTypeDetector.Detect(contentType);
        var fileOrigin = new FileOrigin(
            Clock.Now,
            CurrentUser.Id!.Value,
            CurrentUser.UserName ?? string.Empty,
            contentType,
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
        await _blobContainer.DeleteAsync(document.OriginalFileBlobName);
        await _documentRepository.DeleteAsync(id);
    }

    [Authorize(PaperbasePermissions.Documents.Export)]
    public virtual async Task<IRemoteStreamContent> ExportAsync(GetDocumentListInput input)
    {
        var query = await _documentRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);
        query = ApplySorting(query, input.Sorting);

        var documents = await AsyncExecuter.ToListAsync(query);
        var csv = BuildDocumentCsv(documents);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return new RemoteStreamContent(new MemoryStream(bytes), "documents.csv", "text/csv");
    }

    [Authorize(PaperbasePermissions.Documents.ConfirmClassification)]
    public virtual async Task<DocumentDto> ConfirmClassificationAsync(Guid id, string documentTypeCode)
    {
        var document = await _documentRepository.GetAsync(id);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);

        var metadata = JsonSerializer.Serialize(new { Source = "ManualOverride", ConfirmedBy = CurrentUser.Id });
        await _pipelineRunManager.CompleteClassificationAsync(document, run, documentTypeCode, 1.0, metadata);

        await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            DocumentTypeCode = documentTypeCode,
            ConfidenceScore = 1.0,
            ExtractedText = document.ExtractedText
        });

        await _documentRepository.UpdateAsync(document);
        return ObjectMapper.Map<Document, DocumentDto>(document);
    }

    protected virtual IQueryable<Document> ApplyFilter(IQueryable<Document> query, GetDocumentListInput input)
    {
        if (input.LifecycleStatus.HasValue)
            query = query.Where(x => x.LifecycleStatus == input.LifecycleStatus.Value);

        if (!input.DocumentTypeCode.IsNullOrWhiteSpace())
            query = query.Where(x => x.DocumentTypeCode == input.DocumentTypeCode);

        if (input.NeedsManualReview == true)
        {
            var reviewCodes = new[] { "LowConfidence", "BudgetExceeded" };
            query = query.Where(d => d.PipelineRuns.Any(r =>
                r.PipelineCode == PaperbasePipelines.Classification
                && reviewCodes.Contains(r.ResultCode)
                && r.AttemptNumber == d.PipelineRuns
                    .Where(r2 => r2.PipelineCode == PaperbasePipelines.Classification)
                    .Max(r2 => r2.AttemptNumber)));
        }

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
