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

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 6: MarkPendingReviewAsync sets ReviewStatus to PendingReview
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkPendingReview_Sets_ReviewStatus_To_PendingReview()
    {
        var doc = CreateDocument();
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.None);

        await _manager.MarkPendingReviewAsync(doc);

        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 7: CompleteManualClassificationAsync writes TypeCode, marks Reviewed,
    //             sets Run to Succeeded with ResultCode "ManualOverride"
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteManualClassification_Sets_TypeCode_Marks_Reviewed_And_Completes_Run()
    {
        var doc = CreateDocument();

        // 先模拟低置信度进入待审核
        var run1 = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteAsync(doc, run1, "LowConfidence");
        await _manager.MarkPendingReviewAsync(doc);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        // 人工确认
        var run2 = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteManualClassificationAsync(doc, run2, "contract.general");

        doc.DocumentTypeCode.ShouldBe("contract.general");
        doc.ConfidenceScore.ShouldBe(1.0);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.Reviewed);

        var latestRun = doc.GetLatestRun(PaperbasePipelines.Classification);
        latestRun!.Status.ShouldBe(PipelineRunStatus.Succeeded);
        latestRun.ResultCode.ShouldBe("ManualOverride");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Scenario 8: auto-classification success after PendingReview resets ReviewStatus
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutoClassification_Success_After_PendingReview_Resets_ReviewStatus_To_None()
    {
        var doc = CreateDocument();

        // 第一次低置信度 → PendingReview
        var run1 = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteAsync(doc, run1, "LowConfidence");
        await _manager.MarkPendingReviewAsync(doc);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        // 重试自动分类成功
        var run2 = await _manager.StartAsync(doc, PaperbasePipelines.Classification);
        await _manager.CompleteClassificationAsync(doc, run2, "contract.general", 0.95);

        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.None);
        doc.DocumentTypeCode.ShouldBe("contract.general");
    }
}
