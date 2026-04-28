using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentAppServiceDeleteTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentVectorStore>());
        context.Services.AddSingleton(Substitute.For<IBlobContainer<PaperbaseDocumentContainer>>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
    }
}

/// <summary>
/// Slice 6 守护：删除 Document 时必须显式调用 <see cref="IDocumentVectorStore.DeleteByDocumentIdAsync"/>，
/// 不能只依赖 EF FK cascade —— 否则切到外部 vector store（Azure AI Search / Qdrant）后，
/// 关系数据库删除不会传播到外部索引，旧 chunk 会永远残留。
/// </summary>
public class DocumentAppService_Delete_Tests
    : PaperbaseApplicationTestBase<DocumentAppServiceDeleteTestModule>
{
    private readonly IDocumentAppService _appService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentVectorStore _vectorStore;
    private readonly IBlobContainer<PaperbaseDocumentContainer> _blobContainer;

    public DocumentAppService_Delete_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _vectorStore = GetRequiredService<IDocumentVectorStore>();
        _blobContainer = GetRequiredService<IBlobContainer<PaperbaseDocumentContainer>>();
    }

    [Fact]
    public async Task DeleteAsync_Calls_VectorStore_DeleteByDocumentIdAsync()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);

        await _appService.DeleteAsync(doc.Id);

        await _vectorStore.Received(1).DeleteByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_Also_Removes_Blob_And_Document()
    {
        // 守护其他清理路径仍然在位（blob、aggregate）。Slice 6 只追加 vector store 清理，
        // 不能不小心移除 existing blob/repository delete。
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
            null,
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
