using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Pipelines.Classification;
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
public class DocumentAppServiceReviewTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentTypeRepository>());
        context.Services.AddSingleton(Substitute.For<IBlobContainer<PaperbaseDocumentContainer>>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
    }
}

/// <summary>
/// <see cref="DocumentAppService.ApproveReviewAsync"/> + <see cref="DocumentAppService.RejectReviewAsync"/> 行为测试。
/// <para>
/// 核心验收（CLAUDE.md "OCR 置信度门槛" 承诺）：操作员手动确认通过 → 触发 <c>DocumentReadyEto</c>——
/// 具体分两条路径：
/// <list type="bullet">
///   <item>OCR review（classification 未跑）→ schedule classification pipeline，等其完成自然到 Ready</item>
///   <item>classification 已跑且有 type → 即时 RecomputeLifecycle 直接到 Ready</item>
/// </list>
/// </para>
/// </summary>
public class DocumentAppService_Review_Tests
    : PaperbaseApplicationTestBase<DocumentAppServiceReviewTestModule>
{
    private readonly IDocumentAppService _appService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly DocumentPipelineRunManager _pipelineRunManager;

    public DocumentAppService_Review_Tests()
    {
        _appService = GetRequiredService<IDocumentAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _backgroundJobManager = GetRequiredService<IBackgroundJobManager>();
        _pipelineRunManager = GetRequiredService<DocumentPipelineRunManager>();
    }

    [Fact]
    public async Task ApproveReviewAsync_When_Not_PendingReview_Is_NoOp()
    {
        var doc = CreateDocument();
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.None);  // 初始即非 PendingReview
        StubGet(doc);

        await _appService.ApproveReviewAsync(doc.Id);

        // 幂等：不动状态，不 schedule，不 update
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.None);
        await _backgroundJobManager.DidNotReceive().EnqueueAsync(
            Arg.Any<DocumentClassificationJobArgs>(),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ApproveReviewAsync_OcrReview_Schedules_Classification_When_Not_Run()
    {
        // OCR review 场景：text-extraction Run Succeeded（confidence 不达标），classification 从未跑
        var doc = CreateDocument();
        var textRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _pipelineRunManager.CompleteAsync(doc, textRun);
        doc.MarkPendingOcrReview("OCR confidence 0.40 below threshold 0.80");
        StubGet(doc);

        await _appService.ApproveReviewAsync(doc.Id);

        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.Reviewed);
        doc.ClassificationReason.ShouldBeNull();

        // classification job 被 enqueue（后续完成时由 DeriveLifecycle 自然推进到 Ready）
        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<DocumentClassificationJobArgs>(a => a.DocumentId == doc.Id),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ApproveReviewAsync_ClassificationReview_With_Type_Recomputes_Lifecycle_To_Ready()
    {
        // 分类已完成 + 有 DocumentTypeCode 的 PendingReview 场景（罕见——通常分类置信度低应走 ReclassifyAsync）。
        // 此路径下不重新 schedule classification，而是 RecomputeLifecycle 即时推进到 Ready。
        var doc = CreateDocument();
        var textRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _pipelineRunManager.CompleteAsync(doc, textRun);

        var classRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.Classification);
        // 模拟"分类成功 + 有 type"但人工置 PendingReview 的场景
        typeof(Document)
            .GetMethod("ApplyAutomaticClassificationResult",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(doc, ["host.general", 0.88]);
        await _pipelineRunManager.CompleteAsync(doc, classRun);
        doc.MarkPendingOcrReview("ops manual hold");  // 手工置 PendingReview
        StubGet(doc);

        await _appService.ApproveReviewAsync(doc.Id);

        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.Reviewed);
        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Ready);

        // 不应再 schedule classification（已经跑过）
        await _backgroundJobManager.DidNotReceive().EnqueueAsync(
            Arg.Any<DocumentClassificationJobArgs>(),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task ApproveReviewAsync_ClassificationReview_Without_Type_Keeps_Lifecycle_NonReady()
    {
        // 边界 case：DocumentTypeCode 为 null（分类置信度低）—— ApproveReview 不能让它 Ready。
        // UI 应该用 ReclassifyAsync；如果误调 ApproveReview，文档保持非 Ready（type 空不满足 Ready 条件）。
        var doc = CreateDocument();
        var textRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _pipelineRunManager.CompleteAsync(doc, textRun);

        var classRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.Classification);
        await _pipelineRunManager.CompleteClassificationWithLowConfidenceAsync(doc, classRun, reason: "ambiguous");
        StubGet(doc);

        doc.DocumentTypeCode.ShouldBeNull();
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        await _appService.ApproveReviewAsync(doc.Id);

        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.Reviewed);
        doc.LifecycleStatus.ShouldNotBe(DocumentLifecycleStatus.Ready);  // type 空不能 Ready
    }

    [Fact]
    public async Task RejectReviewAsync_Transitions_Lifecycle_To_Failed()
    {
        var doc = CreateDocument();
        var textRun = await _pipelineRunManager.StartAsync(doc, PaperbasePipelines.TextExtraction);
        await _pipelineRunManager.CompleteAsync(doc, textRun);
        doc.MarkPendingOcrReview("OCR garbage");
        StubGet(doc);

        await _appService.RejectReviewAsync(doc.Id, new RejectReviewInput { Reason = "scan unusable" });

        doc.LifecycleStatus.ShouldBe(DocumentLifecycleStatus.Failed);
        doc.ClassificationReason.ShouldBe("scan unusable");
    }

    private void StubGet(Document doc)
    {
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
    }

    private static Document CreateDocument()
    {
        return new Document(
            Guid.NewGuid(),
            tenantId: null,
            originalFileBlobName: $"blobs/{Guid.NewGuid():N}.pdf",
            sourceType: SourceType.Digital,
            fileOrigin: new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));
    }
}
