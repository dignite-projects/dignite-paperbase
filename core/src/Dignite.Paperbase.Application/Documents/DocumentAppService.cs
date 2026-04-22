using System;
using System.IO;
using System.Threading.Tasks;
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

namespace Dignite.Paperbase.Application.Documents;

public class DocumentAppService : PaperbaseAppService, IDocumentAppService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly SourceTypeDetector _sourceTypeDetector;

    public DocumentAppService(
        IDocumentRepository documentRepository,
        IBlobContainer<PaperbaseDocumentContainer> blobContainer,
        IBackgroundJobManager backgroundJobManager,
        SourceTypeDetector sourceTypeDetector)
    {
        _documentRepository = documentRepository;
        _blobContainer = blobContainer;
        _backgroundJobManager = backgroundJobManager;
        _sourceTypeDetector = sourceTypeDetector;
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

        var totalCount = await _documentRepository.CountAsync();
        var documents = await _documentRepository.GetPagedListAsync(
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting ?? "CreationTime desc");

        return new PagedResultDto<DocumentDto>(
            totalCount,
            ObjectMapper.Map<System.Collections.Generic.List<Document>, System.Collections.Generic.List<DocumentDto>>(documents));
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
}
