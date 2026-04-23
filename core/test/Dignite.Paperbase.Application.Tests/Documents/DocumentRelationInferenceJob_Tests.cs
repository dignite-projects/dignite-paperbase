using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

// ────────────────────────────────────────────────────────────────────────────
// Test module：全量 mock — 无需 EF Core / pgvector
// ────────────────────────────────────────────────────────────────────────────

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentRelationInferenceJobTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentChunkRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentRelationRepository>());
        context.Services.AddSingleton(Substitute.For<IRelationInferrer>());
        context.Services.AddSingleton(Substitute.For<IAiCostLedger>());

        // IFeatureChecker: 让 MonthlyBudgetUsd 返回一个可控预算值（100 美元）
        var featureChecker = Substitute.For<IFeatureChecker>();
        featureChecker
            .GetOrNullAsync(PaperbaseAIFeatures.MonthlyBudgetUsd)
            .Returns("100.00");
        context.Services.AddSingleton(featureChecker);

        context.Services.Configure<PaperbaseAIOptions>(opt =>
        {
            opt.QaTopKChunks = 5;
        });
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Tests
// ────────────────────────────────────────────────────────────────────────────

public class DocumentRelationInferenceJob_Tests : PaperbaseApplicationTestBase<DocumentRelationInferenceJobTestModule>
{
    private readonly DocumentRelationInferenceBackgroundJob _job;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly IRelationInferrer _relationInferrer;
    private readonly IAiCostLedger _costLedger;

    public DocumentRelationInferenceJob_Tests()
    {
        _job = GetRequiredService<DocumentRelationInferenceBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _chunkRepository = GetRequiredService<IDocumentChunkRepository>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _relationInferrer = GetRequiredService<IRelationInferrer>();
        _costLedger = GetRequiredService<IAiCostLedger>();

        // NSubstitute 默认 Task<List<T>> 返回 null，手动设置默认值防止 NPE
        _chunkRepository
            .GetListByDocumentIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());
        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 1: 月度预算超标 → 流水线跳过，IRelationInferrer 不被调用
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Budget_Exceeded_Skips_Inference()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);
        // 测试模块将预算设为 $100；used=$999 > $100 → 触发超额跳过
        _costLedger.GetCurrentMonthUsageAsync(Arg.Any<Guid?>()).Returns(999m);

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _relationInferrer.DidNotReceive()
            .InferAsync(Arg.Any<RelationInferenceRequest>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Skipped);
        latestRun.ResultCode.ShouldBe("BudgetExceeded");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 2: 源文档无 chunks → 流水线跳过（NoChunks）
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Source_Chunks_Skips_Inference()
    {
        var doc = CreateDocument();
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);
        _costLedger.GetCurrentMonthUsageAsync(Arg.Any<Guid?>()).Returns(0m);
        _chunkRepository
            .GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _relationInferrer.DidNotReceive()
            .InferAsync(Arg.Any<RelationInferenceRequest>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Skipped);
        latestRun.ResultCode.ShouldBe("NoChunks");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 3: 向量检索无候选文档 → 流水线完成（NoCandidates）
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Candidate_Docs_Completes_With_NoCandidates()
    {
        var doc = CreateDocument(extractedText: "契約内容...");
        _documentRepository.GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(doc);
        _costLedger.GetCurrentMonthUsageAsync(Arg.Any<Guid?>()).Returns(0m);

        _chunkRepository
            .GetListByDocumentIdAsync(doc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>
            {
                new(Guid.NewGuid(), null, doc.Id, 0, "契約内容...", new float[] { 0.1f, 0.2f })
            });

        // 向量搜索返回空 → 无候选
        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = doc.Id });

        await _relationInferrer.DidNotReceive()
            .InferAsync(Arg.Any<RelationInferenceRequest>(), Arg.Any<CancellationToken>());

        var latestRun = doc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
        latestRun.ResultCode.ShouldBe("NoCandidates");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 4: 推断成功 → 关系写入，流水线标记 Succeeded
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inference_Succeeds_Inserts_Relations_And_Completes_Pipeline()
    {
        var sourceDoc = CreateDocument(extractedText: "業務委託契約書。甲：A社。乙：B社。");
        var candidateDocId = Guid.NewGuid();
        var candidateDoc = CreateDocument(extractedText: "発注書。金額900,000円。");

        _documentRepository.GetAsync(sourceDoc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(sourceDoc);
        _documentRepository.FindAsync(candidateDocId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(candidateDoc);
        _costLedger.GetCurrentMonthUsageAsync(Arg.Any<Guid?>()).Returns(0m);

        var sourceChunk = new DocumentChunk(Guid.NewGuid(), null, sourceDoc.Id, 0, "業務委託契約書。", new float[] { 0.5f, 0.6f });
        var candidateChunk = new DocumentChunk(Guid.NewGuid(), null, candidateDocId, 0, "発注書。", new float[] { 0.5f, 0.6f });

        _chunkRepository
            .GetListByDocumentIdAsync(sourceDoc.Id, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { sourceChunk });

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk> { candidateChunk });

        _relationInferrer
            .InferAsync(Arg.Any<RelationInferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<InferredRelation>
            {
                new() { TargetDocumentId = candidateDocId, RelationType = "references", Confidence = 0.9 }
            });

        await _job.ExecuteAsync(new DocumentRelationInferenceJobArgs { DocumentId = sourceDoc.Id });

        // 关系应已写入
        await _relationRepository.Received(1).InsertAsync(
            Arg.Is<DocumentRelation>(r =>
                r.SourceDocumentId == sourceDoc.Id &&
                r.TargetDocumentId == candidateDocId &&
                r.RelationType == "references" &&
                r.Source == RelationSource.AiSuggested),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        // 流水线状态应为 Succeeded
        var latestRun = sourceDoc.GetLatestRun(PaperbasePipelines.RelationInference);
        latestRun.ShouldNotBeNull();
        latestRun.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helper
    // ────────────────────────────────────────────────────────────────────────

    private static Document CreateDocument(string? extractedText = null)
    {
        var doc = new Document(
            Guid.NewGuid(), null,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedAt: new DateTime(2026, 1, 1),
                uploadedByUserId: Guid.NewGuid(),
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                fileSize: 1024,
                originalFileName: "test.pdf"));

        if (extractedText != null)
        {
            typeof(Document)
                .GetProperty(nameof(Document.ExtractedText))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(doc, [extractedText]);
        }

        return doc;
    }
}
