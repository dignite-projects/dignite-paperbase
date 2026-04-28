using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Events;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
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
    private readonly ILocalEventBus _localEventBus;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;

    public DocumentAppService_Delete_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _localEventBus = GetRequiredService<ILocalEventBus>();
        _blobContainer = GetRequiredService<IBlobContainer<PaperbaseDocumentContainer>>();
    }

    [Fact]
    public async Task DeleteAsync_Publishes_DocumentDeletingEvent()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);

        await _appService.DeleteAsync(doc.Id);

        await _localEventBus.Received(1).PublishAsync(
            Arg.Is<DocumentDeletingEvent>(e =>
                e.DocumentId == doc.Id &&
                e.TenantId == doc.TenantId),
            onUnitOfWorkComplete: false);
    }

    [Fact]
    public async Task DeleteAsync_Also_Removes_Blob_And_Document()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);

        await _appService.DeleteAsync(doc.Id);

        await _blobContainer.Received(1).DeleteAsync(doc.OriginalFileBlobName, Arg.Any<CancellationToken>());
        await _documentRepository.Received(1).DeleteAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
}
