using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Application.Documents.BackgroundJobs;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentEmbeddingJobTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentRepository>());
        context.Services.AddSingleton(Substitute.For<IDocumentKnowledgeIndex>());
        context.Services.AddSingleton(Substitute.For<IBackgroundJobManager>());

        // TextChunker is a real DI-resolved service; replace the workflow itself with a mock
        // so we can return any chunk shape we want without depending on chunker / embedder behavior.
        var workflow = Substitute.ForPartsOf<DocumentEmbeddingWorkflow>(
            new TextChunker(Options.Create(new PaperbaseAIOptions())),
            Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>());
        context.Services.AddSingleton(workflow);
    }
}

/// <summary>
/// DocumentEmbeddingBackgroundJob 行为测试：守护 Slice 6 — 写入路径切换到
/// <see cref="IDocumentKnowledgeIndex"/> 抽象。重点关注：
///   - 重建 embedding 时先 DeleteByDocumentIdAsync 再 UpsertAsync（顺序与幂等性）
///   - DocumentVectorRecord 的 TenantId / DocumentId / DocumentTypeCode 来自 Document 显式拷贝，
///     不依赖 ABP ambient ICurrentTenant —— Hangfire job 场景下 ambient 不一定有值。
/// </summary>
public class DocumentEmbeddingBackgroundJob_Tests
    : PaperbaseApplicationTestBase<DocumentEmbeddingJobTestModule>
{
    private readonly DocumentEmbeddingBackgroundJob _job;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly DocumentEmbeddingWorkflow _workflow;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public DocumentEmbeddingBackgroundJob_Tests()
    {
        _job = GetRequiredService<DocumentEmbeddingBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _vectorStore = GetRequiredService<IDocumentKnowledgeIndex>();
        _workflow = GetRequiredService<DocumentEmbeddingWorkflow>();
        _backgroundJobManager = GetRequiredService<IBackgroundJobManager>();
    }

    [Fact]
    public async Task Empty_ExtractedText_Skips_Job_Without_Touching_VectorStore()
    {
        // ExtractedText 为 null/whitespace 时整个 job 应静默退出，不创建 PipelineRun，
        // 也不能调用向量存储——避免对一个还没有内容的文档清空索引。
        var doc = CreateDocument(extractedText: null);
        SetupDocumentRepository(doc);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _vectorStore.DidNotReceive().DeleteByDocumentIdAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().UpsertAsync(
            Arg.Any<IReadOnlyList<DocumentVectorRecord>>(), Arg.Any<CancellationToken>());

        // 没有 PipelineRun 被启动
        doc.GetLatestRun(PaperbasePipelines.Embedding).ShouldBeNull();
    }

    [Fact]
    public async Task Job_Deletes_Existing_Vectors_Before_Upserting_New_Records()
    {
        // 重建 embedding 必须先清空旧记录再写入新记录——否则 chunkIndex 冲突或残留旧 chunk
        // 会污染后续 RAG 检索结果。Provider 不一定支持 KEY=ID 替换，DELETE-then-INSERT 是
        // 跨 provider 都安全的幂等方案。
        var doc = CreateDocument(extractedText: "契約書本文。");
        SetupDocumentRepository(doc);
        SetupWorkflowChunks([
            new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "chunk-0", Vector = MakeVector(0.1f) }
        ]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        Received.InOrder(async () =>
        {
            await _vectorStore.DeleteByDocumentIdAsync(doc.Id, doc.TenantId, Arg.Any<CancellationToken>());
            await _vectorStore.UpsertAsync(
                Arg.Any<IReadOnlyList<DocumentVectorRecord>>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Job_Maps_Workflow_Output_To_VectorRecord_With_Document_Context()
    {
        // DocumentVectorRecord 的 TenantId / DocumentId / DocumentTypeCode 必须从 Document
        // 显式拷贝，不依赖 ambient context。Title / PageNumber 当前 chunk 没有，置 null
        // （强类型字段，未来 OCR / PDF 元信息接入后再填充）。
        var tenantId = Guid.NewGuid();
        var doc = CreateDocument(
            extractedText: "業務委託契約書。",
            tenantId: tenantId,
            documentTypeCode: "contract.general");
        SetupDocumentRepository(doc);

        var chunk0 = new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "chunk-0", Vector = MakeVector(0.1f) };
        var chunk1 = new DocumentEmbeddingChunk { ChunkIndex = 1, ChunkText = "chunk-1", Vector = MakeVector(0.2f) };
        SetupWorkflowChunks([chunk0, chunk1]);

        IReadOnlyList<DocumentVectorRecord>? capturedRecords = null;
        _vectorStore
            .UpsertAsync(
                Arg.Do<IReadOnlyList<DocumentVectorRecord>>(r => capturedRecords = r),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        capturedRecords.ShouldNotBeNull();
        capturedRecords!.Count.ShouldBe(2);

        var rec0 = capturedRecords[0];
        rec0.TenantId.ShouldBe(tenantId);
        rec0.DocumentId.ShouldBe(doc.Id);
        rec0.DocumentTypeCode.ShouldBe("contract.general");
        rec0.ChunkIndex.ShouldBe(0);
        rec0.Text.ShouldBe("chunk-0");
        rec0.Vector.Span[0].ShouldBe(0.1f);
        rec0.Title.ShouldBeNull();
        rec0.PageNumber.ShouldBeNull();
        rec0.Id.ShouldNotBe(Guid.Empty);

        var rec1 = capturedRecords[1];
        rec1.ChunkIndex.ShouldBe(1);
        rec1.Text.ShouldBe("chunk-1");
        rec1.Vector.Span[0].ShouldBe(0.2f);

        // record IDs 应彼此不同（每个 chunk 一个新 GUID，配合先 Delete 再 Upsert 的幂等模式）
        rec0.Id.ShouldNotBe(rec1.Id);
    }

    [Fact]
    public async Task Job_Skips_Upsert_When_Workflow_Returns_No_Chunks()
    {
        // 极端情况：分块器把短文本切成 0 个 chunk。此时仍要清空旧索引（防止"删除内容后旧 chunk
        // 残留"），但不调用 UpsertAsync（empty list 对部分 provider 是合法的，但显式跳过更稳）。
        var doc = CreateDocument(extractedText: "短文本");
        SetupDocumentRepository(doc);
        SetupWorkflowChunks([]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _vectorStore.Received(1).DeleteByDocumentIdAsync(
            doc.Id, doc.TenantId, Arg.Any<CancellationToken>());
        await _vectorStore.DidNotReceive().UpsertAsync(
            Arg.Any<IReadOnlyList<DocumentVectorRecord>>(), Arg.Any<CancellationToken>());

        var run = doc.GetLatestRun(PaperbasePipelines.Embedding);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    [Fact]
    public async Task Job_Enqueues_RelationInference_After_Successful_Embedding()
    {
        // Pipeline 链：Embedding 完成后入队 RelationInference Job。
        // 这条链路在 Slice 6 的 vector store 重构中不能被破坏。
        var doc = CreateDocument(extractedText: "契約書本文。");
        SetupDocumentRepository(doc);
        SetupWorkflowChunks([
            new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "chunk-0", Vector = MakeVector(0.1f) }
        ]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<DocumentRelationInferenceJobArgs>(a => a.DocumentId == doc.Id),
            Arg.Any<BackgroundJobPriority>(),
            Arg.Any<TimeSpan?>());

        var run = doc.GetLatestRun(PaperbasePipelines.Embedding);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    [Fact]
    public async Task Different_Tenants_Get_Different_TenantId_In_Records()
    {
        // 多租户隔离守护：连续处理两个不同租户的文档，各自的 record.TenantId 必须严格匹配
        // Document.TenantId。如果实现错误地用了 ambient ICurrentTenant，这里就会被检出
        // （测试运行时 ambient tenant = host/null，与两个 doc 的 tenantId 都不同）。
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var docA = CreateDocument(extractedText: "A", tenantId: tenantA);
        var docB = CreateDocument(extractedText: "B", tenantId: tenantB);
        _documentRepository.GetAsync(docA.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(docA);
        _documentRepository.GetAsync(docB.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(docB);

        var allCaptured = new List<IReadOnlyList<DocumentVectorRecord>>();
        _vectorStore
            .UpsertAsync(
                Arg.Do<IReadOnlyList<DocumentVectorRecord>>(r => allCaptured.Add(r)),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        SetupWorkflowChunks([
            new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "x", Vector = MakeVector(0.1f) }
        ]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = docA.Id });
        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = docB.Id });

        allCaptured.Count.ShouldBe(2);
        allCaptured[0].ShouldAllBe(r => r.TenantId == tenantA);
        allCaptured[1].ShouldAllBe(r => r.TenantId == tenantB);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SetupDocumentRepository(Document doc)
    {
        _documentRepository
            .GetAsync(doc.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(doc);
    }

    private void SetupWorkflowChunks(IReadOnlyList<DocumentEmbeddingChunk> chunks)
    {
        _workflow
            .RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(chunks);
    }

    private static float[] MakeVector(float firstValue)
    {
        var v = new float[PaperbaseDbProperties.EmbeddingVectorDimension];
        v[0] = firstValue;
        return v;
    }

    private static Document CreateDocument(
        string? extractedText,
        Guid? tenantId = null,
        string? documentTypeCode = null)
    {
        var doc = new Document(
            Guid.NewGuid(), tenantId,
            $"blobs/{Guid.NewGuid():N}.pdf",
            SourceType.Digital,
            new FileOrigin(
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));

        if (extractedText != null)
        {
            typeof(Document)
                .GetProperty(nameof(Document.ExtractedText))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(doc, [extractedText]);
        }

        if (documentTypeCode != null)
        {
            typeof(Document)
                .GetProperty(nameof(Document.DocumentTypeCode))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(doc, [documentTypeCode]);
        }

        return doc;
    }
}
