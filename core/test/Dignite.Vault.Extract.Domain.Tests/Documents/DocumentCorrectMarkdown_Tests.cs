using System;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents.Pipelines;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Domain-level tests for <see cref="Document.CorrectMarkdown"/> (#555): the operator correction path,
/// distinct from the pipeline's write-once <see cref="Document.SetMarkdown"/>.
/// <see cref="Document.SetMarkdown"/> is <c>internal</c> and is assigned through the manager's public
/// <see cref="DocumentPipelineRunManager.CompleteParseAsync"/>, matching <c>DocumentSanitizationTests</c>.
/// </summary>
public class DocumentCorrectMarkdown_Tests : VaultExtractDomainTestBase<VaultExtractDomainTestModule>
{
    private readonly DocumentPipelineRunManager _manager;

    public DocumentCorrectMarkdown_Tests()
    {
        _manager = GetRequiredService<DocumentPipelineRunManager>();
    }

    private static Document CreateDocument()
    {
        var fileOrigin = new FileOrigin(
            blobName: "blobs/test.pdf",
            uploadedByUserName: "test-user",
            contentType: "application/pdf",
            contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            fileSize: 1024,
            originalFileName: "test.pdf");

        return new Document(
            id: Guid.NewGuid(),
            tenantId: null,
            fileOrigin: fileOrigin);
    }

    private async Task<Document> CompleteExtractionAsync(
        string markdown = "# Doc\n\nbody", string? title = "Doc", string? language = "en")
    {
        var doc = CreateDocument();
        var run = await _manager.StartAsync(doc, VaultExtractPipelines.Parse);
        await _manager.CompleteParseAsync(
            doc, run, markdown: markdown, title: title, language: language,
            extractionMetadata: new DocumentParseMetadata(providerName: "TestProvider", nativePayloadManifest: null));
        return doc;
    }

    [Fact]
    public void CorrectMarkdown_Throws_NotTextExtracted_When_Markdown_Not_Yet_Set()
    {
        var doc = CreateDocument();
        doc.Markdown.ShouldBeNull();

        var ex = Should.Throw<BusinessException>(() => doc.CorrectMarkdown("# Fixed\n\nbody"));

        ex.Code.ShouldBe(VaultExtractErrorCodes.Document.NotTextExtracted);
    }

    [Fact]
    public async Task CorrectMarkdown_Overwrites_An_Existing_Value()
    {
        var doc = await CompleteExtractionAsync(markdown: "# Doc\n\noriginal body");

        doc.CorrectMarkdown("# Doc\n\ncorrected body");

        doc.Markdown.ShouldBe("# Doc\n\ncorrected body");
    }

    [Fact]
    public async Task CorrectMarkdown_Leaves_Title_Language_And_ExtractionMetadata_Untouched()
    {
        var doc = await CompleteExtractionAsync(title: "Original Title", language: "ja");
        var originalTitle = doc.Title;
        var originalLanguage = doc.Language;
        var originalMetadata = doc.ExtractionMetadata;

        doc.CorrectMarkdown("# Doc\n\ncorrected body");

        doc.Title.ShouldBe(originalTitle);
        doc.Language.ShouldBe(originalLanguage);
        doc.ExtractionMetadata.ShouldBe(originalMetadata);
    }

    [Fact]
    public async Task CorrectMarkdown_Leaves_FieldFingerprint_Untouched()
    {
        var doc = await CompleteExtractionAsync();
        doc.SetFieldFingerprint("stable-fingerprint");

        doc.CorrectMarkdown("# Doc\n\ncorrected body");

        doc.FieldFingerprint.ShouldBe("stable-fingerprint");
    }
}
