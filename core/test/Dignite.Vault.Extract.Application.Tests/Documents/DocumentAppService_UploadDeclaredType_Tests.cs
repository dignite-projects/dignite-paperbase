using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Vault.Extract.Documents.Pipelines.Parse;
using Dignite.Vault.Extract.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Vault.Extract.Documents;

[DependsOn(typeof(VaultExtractApplicationTestModule))]
public class DocumentAppServiceUploadDeclaredTypeTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Replace the always-allow IAuthorizationService with a controllable grant set (same pattern as
        // SchemaReadAuthorization_Tests / GrantSetAuthorizationService), so the "caller lacks
        // ConfirmClassification" acceptance scenario can actually deny it. Default grant covers both
        // permissions; individual tests narrow it.
        var authorizationService = new GrantSetAuthorizationService
        {
            Granted = new HashSet<string>
            {
                VaultExtractPermissions.Documents.Upload,
                VaultExtractPermissions.Documents.ConfirmClassification
            }
        };
        context.Services.AddSingleton(authorizationService);
        context.Services.RemoveAll<IAuthorizationService>();
        context.Services.RemoveAll<IAbpAuthorizationService>();
        context.Services.AddSingleton<IAuthorizationService>(authorizationService);
        context.Services.AddSingleton<IAbpAuthorizationService>(authorizationService);

        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentTypeRepository>());
        context.Services.AddSingleton(Substitute.For<IFieldRepository>());
        context.Services.AddSingleton(Substitute.For<ICabinetRepository>());
        context.Services.AddSingleton(Substitute.For<IBlobContainer<VaultExtractDocumentContainer>>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
    }
}

/// <summary>
/// #623: covers UploadAsync's declared-DocumentTypeId acceptance list (the Upload-time half only — the
/// Parse-cascade completion of the Classification stage is covered by
/// <c>DocumentParseBackgroundJob_DeclaredType_Tests</c>, which needs a different dependency set).
/// </summary>
public class DocumentAppService_UploadDeclaredType_Tests
    : VaultExtractApplicationTestBase<DocumentAppServiceUploadDeclaredTypeTestModule>
{
    private readonly IDocumentAppService _appService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IBlobContainer<VaultExtractDocumentContainer> _blobContainer;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly GrantSetAuthorizationService _authorization;

    public DocumentAppService_UploadDeclaredType_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _blobContainer = GetRequiredService<IBlobContainer<VaultExtractDocumentContainer>>();
        _backgroundJobManager = GetRequiredService<IBackgroundJobManager>();
        _authorization = GetRequiredService<GrantSetAuthorizationService>();

        // UploadAsync precondition fail-fast check (mirrors DocumentAppService_Delete_Tests).
        _documentTypeRepository.GetCountAsync(Arg.Any<CancellationToken>()).Returns(1L);
        // MapToDtoAsync's ResolveReferenceMapsAsync always looks up field definitions for a document's type;
        // harmless no-op default for tests whose document ends up with a DocumentTypeId set.
        _fieldRepository.GetListAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<Field, bool>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private void Grant(params string[] permissions) => _authorization.Granted = new HashSet<string>(permissions);

    [Fact]
    public async Task UploadAsync_With_Valid_DocumentTypeId_Declares_The_Type_As_Confirmed()
    {
        var type = new DocumentType(Guid.NewGuid(), null, "invoice.general", "Invoice");
        _documentTypeRepository.FindAsync(type.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(type);
        // MapToDtoAsync's ResolveReferenceMapsAsync resolves DocumentTypeCode for the returned DTO once
        // DocumentTypeId is set; stub the predicate-based lookup it uses.
        _documentTypeRepository.GetListAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<DocumentType, bool>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns([type]);

        var input = CreateUploadInput([1, 2, 3]);
        input.DocumentTypeId = type.Id;

        await _appService.UploadAsync(input);

        // #623 decision 1: declaring a type is equivalent to an operator confirmation -- confidence 1.0,
        // Confirmed disposition, no UnresolvedClassification review reason -- applied synchronously at
        // upload, before any pipeline has run.
        await _documentRepository.Received(1).InsertAsync(
            Arg.Is<Document>(d =>
                d.DocumentTypeId == type.Id &&
                d.ClassificationConfidence == 1.0 &&
                d.ReviewDisposition == DocumentReviewDisposition.Confirmed &&
                (d.ReviewReasons & DocumentReviewReasons.UnresolvedClassification) == DocumentReviewReasons.None),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // Parse is still enqueued exactly as before -- the LLM classification short-circuit happens later,
        // in the Parse job's cascade branch (#623 decision 2), not at upload time.
        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Any<DocumentParseJobArgs>(), Arg.Any<BackgroundJobPriority>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task UploadAsync_Throws_AbpAuthorizationException_When_Caller_Lacks_ConfirmClassification()
    {
        Grant(VaultExtractPermissions.Documents.Upload); // Upload only, no ConfirmClassification.

        var input = CreateUploadInput([1, 2, 3]);
        input.DocumentTypeId = Guid.NewGuid();

        await Should.ThrowAsync<AbpAuthorizationException>(() => _appService.UploadAsync(input));

        await _documentRepository.DidNotReceive().InsertAsync(
            Arg.Any<Document>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _blobContainer.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_Throws_EntityNotFoundException_When_DocumentTypeId_Does_Not_Resolve()
    {
        // No FindAsync stub for this id -> mock default returns null, representing an unknown id or a
        // cross-layer id filtered out by the ambient IMultiTenant filter.
        var input = CreateUploadInput([1, 2, 3]);
        input.DocumentTypeId = Guid.NewGuid();

        await Should.ThrowAsync<EntityNotFoundException>(() => _appService.UploadAsync(input));

        await _documentRepository.DidNotReceive().InsertAsync(
            Arg.Any<Document>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _blobContainer.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_Without_DocumentTypeId_Behaves_Exactly_As_Before()
    {
        await _appService.UploadAsync(CreateUploadInput([1, 2, 3]));

        await _documentRepository.Received(1).InsertAsync(
            Arg.Is<Document>(d =>
                d.DocumentTypeId == null &&
                d.ClassificationConfidence == 0 &&
                d.ReviewDisposition == DocumentReviewDisposition.NotReviewed),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Any<DocumentParseJobArgs>(), Arg.Any<BackgroundJobPriority>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public void DeclareDocumentType_Throws_When_DocumentTypeId_Already_Set()
    {
        var doc = CreateDocument();
        InvokeDeclareDocumentType(doc, Guid.NewGuid());

        var exception = Should.Throw<TargetInvocationException>(
            () => InvokeDeclareDocumentType(doc, Guid.NewGuid()));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void DeclareDocumentType_Throws_When_Markdown_Already_Set()
    {
        var doc = CreateDocument();
        typeof(Document).GetProperty(nameof(Document.Markdown))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(doc, ["# Already extracted"]);

        var exception = Should.Throw<TargetInvocationException>(
            () => InvokeDeclareDocumentType(doc, Guid.NewGuid()));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void RetractDeclaredType_Throws_When_No_Type_Is_Declared()
    {
        var doc = CreateDocument();

        var exception = Should.Throw<TargetInvocationException>(() => InvokeRetractDeclaredType(doc));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void RetractDeclaredType_Resets_Declared_State_Back_To_Not_Reviewed()
    {
        var doc = CreateDocument();
        InvokeDeclareDocumentType(doc, Guid.NewGuid());

        InvokeRetractDeclaredType(doc);

        doc.DocumentTypeId.ShouldBeNull();
        doc.ClassificationConfidence.ShouldBe(0d);
        doc.ReviewDisposition.ShouldBe(DocumentReviewDisposition.NotReviewed);
        doc.ReviewReasons.ShouldBe(DocumentReviewReasons.None);
    }

    private static void InvokeDeclareDocumentType(Document document, Guid documentTypeId)
    {
        typeof(Document)
            .GetMethod("DeclareDocumentType", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(document, [documentTypeId]);
    }

    private static void InvokeRetractDeclaredType(Document document)
    {
        typeof(Document)
            .GetMethod("RetractDeclaredType", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(document, null);
    }

    private static Document CreateDocument()
    {
        return new Document(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new FileOrigin(
                blobName: $"blobs/{Guid.NewGuid():N}.pdf",
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));
    }

    private static UploadDocumentInput CreateUploadInput(
        byte[] bytes, string fileName = "A.pdf", string contentType = "application/pdf")
    {
        return new UploadDocumentInput
        {
            File = new RemoteStreamContent(
                new MemoryStream(bytes),
                fileName,
                contentType,
                disposeStream: true)
        };
    }
}
