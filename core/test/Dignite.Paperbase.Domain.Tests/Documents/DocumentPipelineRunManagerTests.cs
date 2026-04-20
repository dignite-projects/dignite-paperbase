using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentPipelineRunManagerTests : PaperbaseDomainTestBase<PaperbaseDomainTestModule>
{
    private readonly DocumentPipelineRunManager _manager;

    public DocumentPipelineRunManagerTests()
    {
        _manager = GetRequiredService<DocumentPipelineRunManager>();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static Document CreateDocument()
    {
        var fileOrigin = new FileOrigin(
            uploadedAt: new DateTime(2026, 1, 1),
            uploadedByUserId: Guid.NewGuid(),
            uploadedByUserName: "test-user",
            contentType: "application/pdf",
            fileSize: 1024,
            originalFileName: "test.pdf");

        return new Document(
            id: Guid.NewGuid(),
            tenantId: null,
            originalFileBlobName: "blobs/test.pdf",
            sourceType: SourceType.Digital,
            fileOrigin: fileOrigin);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 1: all key pipelines succeed → Ready
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task All_KeyPipelines_Succeed_Transitions_To_Ready()
    {
        var doc = CreateDocument();
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Uploaded);

        // TextExtraction
        var textRun = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Processing);

        await _manager.CompleteAsync(doc, textRun, resultCode: "Ok");
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Processing); // Classification not done

        // Classification
        var classRun = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteAsync(doc, classRun, resultCode: "Ok");

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Ready);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 2: key pipeline (TextExtraction) fails → Failed
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TextExtraction_Fail_Transitions_To_Failed()
    {
        var doc = CreateDocument();

        var run = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _manager.FailAsync(doc, run, errorMessage: "OCR engine error");

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Failed);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 3: retry increments AttemptNumber; latest run state takes effect
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_Increments_AttemptNumber_And_Latest_Run_State_Wins()
    {
        var doc = CreateDocument();

        // Attempt 1 — fail
        var run1 = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        run1.AttemptNumber.ShouldBe(1);
        await _manager.FailAsync(doc, run1, errorMessage: "timeout");

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Failed);

        // Attempt 2 — succeed (retry)
        var run2 = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        run2.AttemptNumber.ShouldBe(2);

        // While running, LifecycleStatus goes back to Processing (latest is Running)
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Processing);

        await _manager.CompleteAsync(doc, run2, resultCode: "Ok");

        // Latest run for TextExtraction is now Succeeded; Classification still missing → Processing
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Processing);

        // Verify GetLatestRun returns attempt 2
        var latestRun = doc.GetLatestRun(PaperbasePipelines.TextExtraction);
        latestRun.ShouldNotBeNull();
        latestRun.AttemptNumber.ShouldBe(2);
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 4: non-key pipeline (Embedding) fails → still Ready
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonKey_Pipeline_Failure_Does_Not_Prevent_Ready()
    {
        var doc = CreateDocument();

        // Complete both key pipelines
        var textRun = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _manager.CompleteAsync(doc, textRun);

        var classRun = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteAsync(doc, classRun);

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Ready);

        // Fail non-key pipeline
        var embeddingRun = await _manager.StartAsync(doc, PaperbasePipelines.Embedding);
        await _manager.FailAsync(doc, embeddingRun, errorMessage: "vector store unavailable");

        // LifecycleStatus must remain Ready — Embedding is not a key pipeline
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Ready);
        doc.HasEmbedding.ShouldBeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 5: skip a non-key pipeline — does not block Ready
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Skipping_NonKey_Pipeline_Does_Not_Block_Ready()
    {
        var doc = CreateDocument();

        var textRun = await _manager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _manager.CompleteAsync(doc, textRun);

        var classRun = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteAsync(doc, classRun);

        var embeddingRun = await _manager.StartAsync(doc, PaperbasePipelines.Embedding);
        await _manager.SkipAsync(doc, embeddingRun, reason: "document too short");

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Ready);
    }
}
