using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Pipelines.Classification;
using Dignite.Paperbase.Documents.Pipelines.Embedding;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentClassificationJobTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDistributedEventBus>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());

        var workflow = Substitute.ForPartsOf<DocumentClassificationWorkflow>(
            Substitute.For<IChatClient>(),
            Options.Create(new PaperbaseAIOptions()),
            new DefaultPromptProvider());
        context.Services.AddSingleton(workflow);

        // 注册一个合同类型，阈值 0.75，含关键词"契約書"供关键词分类器使用
        context.Services.Configure<DocumentTypeOptions>(opts =>
        {
            opts.Register(new DocumentTypeDefinition("contract.general", "合同")
            {
                MatchKeywords = new List<string> { "契約書" },
                ConfidenceThreshold = 0.75
            });
        });

        context.Services.Configure<PaperbaseAIOptions>(_ => { });
    }
}

/// <summary>
/// DocumentClassificationBackgroundJob 行为测试：验证分类结果如何驱动
/// PipelineRun 状态流转、DocumentClassifiedEto 发布与 EmbeddingJob 入队。
/// IChatClient 和 DocumentClassificationWorkflow 均使用 NSubstitute 替代，无真实 LLM 调用。
/// </summary>
public class DocumentClassificationBackgroundJob_Tests
    : PaperbaseApplicationTestBase<DocumentClassificationJobTestModule>
{
    private readonly DocumentClassificationBackgroundJob _job;
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentClassificationWorkflow _workflow;
    private readonly IDistributedEventBus _eventBus;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public DocumentClassificationBackgroundJob_Tests()
    {
        _job = GetRequiredService<DocumentClassificationBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _workflow = GetRequiredService<DocumentClassificationWorkflow>();
        _eventBus = GetRequiredService<IDistributedEventBus>();
        _backgroundJobManager = GetRequiredService<IBackgroundJobManager>();
    }

    [Fact]
    public async Task HighConfidence_Completes_Pipeline_Publishes_Event_Enqueues_Embedding()
    {
        var doc = CreateDocument("業務委託契約書の内容です。");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DocumentClassificationOutcome
            {
                TypeCode = "contract.general",
                ConfidenceScore = 0.92,   // 超过阈值 0.75
                Reason = "Contains contract keywords"
            });

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        // PipelineRun 以 Succeeded 完成
        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        // Document 的 TypeCode/ClassificationConfidence 已写入，ReviewStatus 重置为 None
        doc.DocumentTypeCode.ShouldBe("contract.general");
        doc.ClassificationConfidence.ShouldBe(0.92);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.None);

        // 发布了 DocumentClassifiedEto
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<DocumentClassifiedEto>(e =>
                e.DocumentId == doc.Id &&
                e.DocumentTypeCode == "contract.general" &&
                e.ClassificationConfidence == 0.92),
            Arg.Any<bool>());

        // 入队了 Embedding Job
        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<DocumentEmbeddingJobArgs>(a => a.DocumentId == doc.Id),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task LowConfidence_Marks_PendingReview_No_Event_No_Embedding()
    {
        var doc = CreateDocument("Some document text.");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DocumentClassificationOutcome
            {
                TypeCode = "contract.general",
                ConfidenceScore = 0.50,   // 低于阈值 0.75
                Reason = "Low confidence"
            });

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        // DocumentTypeCode/ClassificationConfidence 不应被污染，ReviewStatus 应为 PendingReview
        doc.DocumentTypeCode.ShouldBeNull();
        doc.ClassificationConfidence.ShouldBe(0);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        // 不发布事件，不入队 Embedding
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<DocumentClassifiedEto>(), Arg.Any<bool>());
        await _backgroundJobManager.DidNotReceive().EnqueueAsync(
            Arg.Any<DocumentEmbeddingJobArgs>(),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task NullTypeCode_Marks_PendingReview()
    {
        var doc = CreateDocument("Unrecognized document.");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DocumentClassificationOutcome
            {
                TypeCode = null,
                ConfidenceScore = 0.0
            });

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);
        doc.DocumentTypeCode.ShouldBeNull();
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);
    }

    [Fact]
    public async Task UnregisteredTypeCode_Marks_PendingReview_Without_Polluting_Document()
    {
        // LLM 幻觉：返回一个不在 DocumentTypeOptions 注册表中的 TypeCode
        var doc = CreateDocument("Some document text.");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new DocumentClassificationOutcome
            {
                TypeCode = "invoice.general",   // 未注册——白名单不应放行
                ConfidenceScore = 0.95,         // 看似高置信度，但 TypeCode 非法
                Reason = "LLM hallucinated an unknown type"
            });

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        // 不能写入未注册的 TypeCode，必须落入 PendingReview
        doc.DocumentTypeCode.ShouldBeNull();
        doc.ClassificationConfidence.ShouldBe(0);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        // 不发布事件，不入队 Embedding
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<DocumentClassifiedEto>(), Arg.Any<bool>());
        await _backgroundJobManager.DidNotReceive().EnqueueAsync(
            Arg.Any<DocumentEmbeddingJobArgs>(),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task AiProviderTimeout_FallsBack_To_Keyword_Classifier_And_Succeeds()
    {
        // 文本包含关键词"契約書"，关键词分类器可命中
        var doc = CreateDocument("業務委託契約書。甲：A社。乙：B社。");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<DocumentClassificationOutcome>(_ => throw new TimeoutException("AI service timeout"));

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        // 关键词分类器返回 confidence 0.9 > threshold 0.75 → 分类成功
        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<DocumentClassifiedEto>(e => e.DocumentTypeCode == "contract.general"),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task AiProviderTimeout_KeywordNoMatch_FallsBackToLowConfidence()
    {
        // 文本不含任何已注册的关键词
        var doc = CreateDocument("This is a quarterly report with no contract keywords.");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<DocumentClassificationOutcome>(_ => throw new TimeoutException("AI service timeout"));

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<DocumentClassifiedEto>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task HttpRequestException_FallsBack_To_Keyword_Classifier()
    {
        // HTTP 通信故障是 transient provider error，关键词兜底是合理替代。
        // 旧实现用 ex.GetType().Name.Contains("HttpRequestException") 字符串匹配，
        // 现在改为 typed catch (HttpRequestException) — 这个测试锁定新路径。
        var doc = CreateDocument("業務委託契約書。甲：A社。乙：B社。");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<DocumentClassificationOutcome>(_ => throw new HttpRequestException("503 service unavailable"));

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        // 关键词兜底命中 contract.general
        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<DocumentClassifiedEto>(e => e.DocumentTypeCode == "contract.general"),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task JsonException_Routes_To_PendingReview_Even_When_Keyword_Would_Match()
    {
        // 关键回归测试：schema 漂移类异常（JsonException）必须直接走 PendingReview，
        // 不能用关键词兜底替代——关键词命中只反映文本表面，无法弥补 LLM 语义判定失效。
        // 文本故意包含已注册关键词 "契約書"，如果错误地走了关键词兜底路径，文档会被
        // 高置信度分类成 contract.general，测试断言失败。正确实现下，文档落入
        // PendingReview，TypeCode 为 null。
        var doc = CreateDocument("業務委託契約書。甲：A社。乙：B社。");
        SetupDocumentRepository(doc);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<DocumentClassificationOutcome>(_ => throw new JsonException("schema mismatch: missing required field"));

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        var run = doc.GetLatestRun(PaperbasePipelines.Classification);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);

        // 没有走关键词兜底——TypeCode 为 null，ReviewStatus 为 PendingReview
        doc.DocumentTypeCode.ShouldBeNull();
        doc.ClassificationConfidence.ShouldBe(0);
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        // 不发布事件，不入队 Embedding
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<DocumentClassifiedEto>(), Arg.Any<bool>());
        await _backgroundJobManager.DidNotReceive().EnqueueAsync(
            Arg.Any<DocumentEmbeddingJobArgs>(),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Wrapped_JsonException_Also_Routes_To_PendingReview()
    {
        // SDK 把 JsonException 包在外层异常里也算 schema drift。
        var doc = CreateDocument("業務委託契約書。");
        SetupDocumentRepository(doc);

        var inner = new JsonException("invalid token");
        var outer = new InvalidOperationException("LLM response could not be parsed", inner);

        _workflow
            .RunAsync(
                Arg.Any<IReadOnlyList<DocumentTypeDefinition>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<DocumentClassificationOutcome>(_ => throw outer);

        await _job.ExecuteAsync(new DocumentClassificationJobArgs { DocumentId = doc.Id });

        doc.DocumentTypeCode.ShouldBeNull();
        doc.ReviewStatus.ShouldBe(DocumentReviewStatus.PendingReview);

        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<DocumentClassifiedEto>(), Arg.Any<bool>());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SetupDocumentRepository(Document doc)
    {
        _documentRepository
            .GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
    }

    private static Document CreateDocument(string extractedText)
    {
        var doc = new Document(
            Guid.NewGuid(), null,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));

        typeof(Document)
            .GetProperty(nameof(Document.Markdown))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(doc, [extractedText]);

        return doc;
    }
}
