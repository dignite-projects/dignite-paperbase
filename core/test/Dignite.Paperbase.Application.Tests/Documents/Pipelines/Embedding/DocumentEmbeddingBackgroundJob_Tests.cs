using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Pipelines.Embedding;
using Dignite.Paperbase.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
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

        // TextChunker is a real DI-resolved service; replace the workflow itself with a mock
        // so we can return any chunk shape we want without depending on chunker / embedder behavior.
        var workflow = Substitute.ForPartsOf<DocumentEmbeddingWorkflow>(
            new TextChunker(Options.Create(new PaperbaseAIOptions())),
            Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>());
        context.Services.AddSingleton(workflow);
    }
}

/// <summary>
/// DocumentEmbeddingBackgroundJob 行为测试：守护 Slice G — 写入路径切换到
/// <see cref="IDocumentKnowledgeIndex.UpsertDocumentAsync"/> 整文档替换语义。重点关注：
    ///   - UpsertDocumentAsync 携带完整 DocumentId / TenantId / DocumentTypeCode / Chunks
///   - TenantId 来自 Document 显式拷贝，不依赖 ABP ambient ICurrentTenant
///   - 空 chunks 时 UpsertDocumentAsync 仍被调用（传入空 Chunks，由实现清除旧索引）
/// </summary>
public class DocumentEmbeddingBackgroundJob_Tests
    : PaperbaseApplicationTestBase<DocumentEmbeddingJobTestModule>
{
    private readonly DocumentEmbeddingBackgroundJob _job;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentKnowledgeIndex _knowledgeIndex;
    private readonly DocumentEmbeddingWorkflow _workflow;

    public DocumentEmbeddingBackgroundJob_Tests()
    {
        _job = GetRequiredService<DocumentEmbeddingBackgroundJob>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _knowledgeIndex = GetRequiredService<IDocumentKnowledgeIndex>();
        _workflow = GetRequiredService<DocumentEmbeddingWorkflow>();
    }

    [Fact]
    public async Task Empty_ExtractedText_Skips_Job_Without_Touching_KnowledgeIndex()
    {
        // ExtractedText 为 null/whitespace 时整个 job 应静默退出，不创建 PipelineRun，
        // 也不能调用向量存储——避免对一个还没有内容的文档清空索引。
        var doc = CreateDocument(extractedText: null);
        SetupDocumentRepository(doc);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _knowledgeIndex.DidNotReceive().UpsertDocumentAsync(
            Arg.Any<DocumentVectorIndexUpdate>(), Arg.Any<CancellationToken>());

        doc.GetLatestRun(PaperbasePipelines.Embedding).ShouldBeNull();
    }

    [Fact]
    public async Task Job_Calls_UpsertDocumentAsync_With_Chunks()
    {
        // UpsertDocumentAsync は 削除 + 挿入 + DocumentVector upsert を原子的に行う。
        // job からは1回呼ぶだけでよい。
        var doc = CreateDocument(extractedText: "契約書本文。");
        SetupDocumentRepository(doc);
        SetupWorkflowChunks([
            new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "chunk-0", Vector = MakeVector(0.1f) }
        ]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _knowledgeIndex.Received(1).UpsertDocumentAsync(
            Arg.Is<DocumentVectorIndexUpdate>(u =>
                u.DocumentId == doc.Id &&
                u.TenantId == doc.TenantId &&
                u.Chunks.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Job_Maps_Workflow_Output_To_VectorRecord_With_Document_Context()
    {
        // DocumentVectorIndexUpdate の TenantId / DocumentId / DocumentTypeCode は Document から
        // 明示的にコピーされ、ambient context に依存しない（Hangfire job 安全性）。
        var tenantId = Guid.NewGuid();
        var doc = CreateDocument(
            extractedText: "業務委託契約書。",
            tenantId: tenantId,
            documentTypeCode: "contract.general");
        SetupDocumentRepository(doc);

        var chunk0 = new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "chunk-0", Vector = MakeVector(0.1f) };
        var chunk1 = new DocumentEmbeddingChunk { ChunkIndex = 1, ChunkText = "chunk-1", Vector = MakeVector(0.2f) };
        SetupWorkflowChunks([chunk0, chunk1]);

        DocumentVectorIndexUpdate? capturedUpdate = null;
        _knowledgeIndex
            .UpsertDocumentAsync(
                Arg.Do<DocumentVectorIndexUpdate>(u => capturedUpdate = u),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        capturedUpdate.ShouldNotBeNull();
        capturedUpdate!.DocumentId.ShouldBe(doc.Id);
        capturedUpdate.TenantId.ShouldBe(tenantId);
        capturedUpdate.DocumentTypeCode.ShouldBe("contract.general");
        capturedUpdate.Chunks.Count.ShouldBe(2);

        var rec0 = capturedUpdate.Chunks[0];
        rec0.ChunkIndex.ShouldBe(0);
        rec0.Text.ShouldBe("chunk-0");
        rec0.Vector.Span[0].ShouldBe(0.1f);
        rec0.PageNumber.ShouldBeNull();

        var rec1 = capturedUpdate.Chunks[1];
        rec1.ChunkIndex.ShouldBe(1);
        rec1.Text.ShouldBe("chunk-1");
        rec1.Vector.Span[0].ShouldBe(0.2f);
    }

    [Fact]
    public async Task Job_Calls_UpsertDocumentAsync_With_Empty_Chunks_When_Workflow_Returns_None()
    {
        // 分块器が0件を返しても UpsertDocumentAsync は呼ばれる（空 Chunks で呼ぶことで
        // 実装側が旧インデックスを削除する）。
        var doc = CreateDocument(extractedText: "短文本");
        SetupDocumentRepository(doc);
        SetupWorkflowChunks([]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = doc.Id });

        await _knowledgeIndex.Received(1).UpsertDocumentAsync(
            Arg.Is<DocumentVectorIndexUpdate>(u =>
                u.DocumentId == doc.Id &&
                u.Chunks.Count == 0),
            Arg.Any<CancellationToken>());

        var run = doc.GetLatestRun(PaperbasePipelines.Embedding);
        run.ShouldNotBeNull();
        run.Status.ShouldBe(PipelineRunStatus.Succeeded);
    }

    [Fact]
    public async Task Different_Tenants_Get_Different_TenantId_In_Update()
    {
        // 多租户隔離：连续两份文档的 UpsertDocumentAsync.TenantId 必须严格匹配各自的 Document.TenantId。
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var docA = CreateDocument(extractedText: "A", tenantId: tenantA);
        var docB = CreateDocument(extractedText: "B", tenantId: tenantB);
        _documentRepository.GetAsync(docA.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(docA);
        _documentRepository.GetAsync(docB.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(docB);

        var capturedUpdates = new List<DocumentVectorIndexUpdate>();
        _knowledgeIndex
            .UpsertDocumentAsync(
                Arg.Do<DocumentVectorIndexUpdate>(u => capturedUpdates.Add(u)),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        SetupWorkflowChunks([
            new DocumentEmbeddingChunk { ChunkIndex = 0, ChunkText = "x", Vector = MakeVector(0.1f) }
        ]);

        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = docA.Id });
        await _job.ExecuteAsync(new DocumentEmbeddingJobArgs { DocumentId = docB.Id });

        capturedUpdates.Count.ShouldBe(2);
        capturedUpdates[0].TenantId.ShouldBe(tenantA);
        capturedUpdates[1].TenantId.ShouldBe(tenantB);
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
        // Reflects PaperbaseRagOptions default value, not runtime config.
        var v = new float[new PaperbaseRagOptions().EmbeddingDimension];
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
