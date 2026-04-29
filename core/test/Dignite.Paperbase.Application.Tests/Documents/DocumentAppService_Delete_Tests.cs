using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Events;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentAppServiceDeleteTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentRelationRepository>());
        context.Services.AddSingleton(Substitute.For<IBlobContainer<PaperbaseDocumentContainer>>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
        context.Services.AddSingleton(Substitute.For<ILocalEventBus>());
    }
}

/// <summary>
/// Slice E 守护：<see cref="DocumentAppService.DeleteAsync"/> 必须
/// 通过 <see cref="ILocalEventBus"/> 发布 <see cref="DocumentDeletingEvent"/>
/// 以触发 after-commit chunk 清理，而非在主 UoW 内直接调用 vector store。
/// </summary>
public class DocumentAppService_Delete_Tests
    : PaperbaseApplicationTestBase<DocumentAppServiceDeleteTestModule>
{
    private readonly IDocumentAppService _appService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly ILocalEventBus _localEventBus;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;

    public DocumentAppService_Delete_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _localEventBus = GetRequiredService<ILocalEventBus>();
        _distributedEventBus = GetRequiredService<IDistributedEventBus>();
        _blobContainer = GetRequiredService<IBlobContainer<PaperbaseDocumentContainer>>();
    }

    [Fact]
    public async Task DeleteAsync_Publishes_DocumentDeletingEvent()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
        _relationRepository.GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentRelation>());

        await _appService.DeleteAsync(doc.Id);

        await _localEventBus.Received(1).PublishAsync(
            Arg.Is<DocumentDeletingEvent>(e =>
                e.DocumentId == doc.Id &&
                e.TenantId == doc.TenantId),
            onUnitOfWorkComplete: false);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_Document_Without_Removing_Blob()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
        _relationRepository.GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentRelation>());

        await _appService.DeleteAsync(doc.Id);

        await _blobContainer.DidNotReceive().DeleteAsync(doc.OriginalFileBlobName, Arg.Any<CancellationToken>());
        await _documentRepository.Received(1).DeleteAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_Publishes_DocumentDeletedEto()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
        _relationRepository.GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentRelation>());

        await _appService.DeleteAsync(doc.Id);

        await _distributedEventBus.Received(1).PublishAsync(
            Arg.Is<DocumentDeletedEto>(e =>
                e.DocumentId == doc.Id &&
                e.TenantId == doc.TenantId &&
                e.DocumentTypeCode == doc.DocumentTypeCode),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task RestoreAsync_Restores_Deleted_Document_And_Publishes_Event()
    {
        var doc = CreateDocument();
        doc.IsDeleted = true;
        doc.DeletionTime = DateTime.UtcNow;
        doc.DeleterId = Guid.NewGuid();

        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);

        await _appService.RestoreAsync(doc.Id);

        doc.IsDeleted.ShouldBeFalse();
        doc.DeletionTime.ShouldBeNull();
        doc.DeleterId.ShouldBeNull();
        await _documentRepository.Received(1).UpdateAsync(doc, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _distributedEventBus.Received(1).PublishAsync(
            Arg.Is<DocumentRestoredEto>(e =>
                e.DocumentId == doc.Id &&
                e.TenantId == doc.TenantId &&
                e.DocumentTypeCode == doc.DocumentTypeCode),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task UploadAsync_Throws_Duplicate_When_ContentHash_Belongs_To_Active_Document()
    {
        var existing = CreateDocumentWithContent([1, 2, 3]);
        _documentRepository.FindByContentHashAsync(
                existing.FileOrigin.ContentHash,
                Arg.Any<CancellationToken>())
            .Returns(existing);

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _appService.UploadAsync(CreateUploadInput([1, 2, 3]));
        });

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentDuplicate);
    }

    [Fact]
    public async Task UploadAsync_Throws_RecycleBin_Error_When_ContentHash_Belongs_To_Deleted_Document()
    {
        var existing = CreateDocumentWithContent([1, 2, 3]);
        existing.IsDeleted = true;
        _documentRepository.FindByContentHashAsync(
                existing.FileOrigin.ContentHash,
                Arg.Any<CancellationToken>())
            .Returns(existing);

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _appService.UploadAsync(CreateUploadInput([1, 2, 3]));
        });

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentInRecycleBin);
        exception.Data["ExistingDocumentId"].ShouldBe(existing.Id);
    }

    private static Document CreateDocument()
    {
        return new Document(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));
    }

    private static Document CreateDocumentWithContent(byte[] bytes)
    {
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();

        return new Document(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: contentHash,
                fileSize: bytes.LongLength,
                originalFileName: "test.pdf"));
    }

    private static UploadDocumentInput CreateUploadInput(byte[] bytes)
    {
        return new UploadDocumentInput
        {
            File = new RemoteStreamContent(
                new MemoryStream(bytes),
                "A.pdf",
                "application/pdf",
                disposeStream: true)
        };
    }
}
