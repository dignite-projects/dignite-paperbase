using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Dignite.Paperbase.Documents;
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
public class DocumentQaAppServiceTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // GlobalAskAsync 不会调用 _documentRepository，但 DocumentQaAppService 构造
        // 函数依赖它，必须注册 mock 才能由 DI 容器构造。
        context.Services.AddSingleton(
            Substitute.For<IDocumentRepository>());

        context.Services.AddSingleton(
            Substitute.For<IDocumentKnowledgeIndex>());

        context.Services.AddSingleton(
            Substitute.For<IChatClient>());

        context.Services.AddSingleton(
            Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>());

        // 用 Substitute 替换默认 Workflow，便于断言调用与返回
        var qaWorkflow = Substitute.ForPartsOf<DocumentQaWorkflow>(
            Substitute.For<IChatClient>(),
            Microsoft.Extensions.Options.Options.Create(new PaperbaseAIOptions { QaTopKChunks = 5 }),
            new DefaultPromptProvider());
        context.Services.AddSingleton(qaWorkflow);

        var rerankWorkflow = Substitute.ForPartsOf<DocumentRerankWorkflow>(
            Substitute.For<IChatClient>(),
            Microsoft.Extensions.Options.Options.Create(new PaperbaseAIOptions { QaTopKChunks = 5 }),
            new DefaultPromptProvider());
        context.Services.AddSingleton(rerankWorkflow);

        context.Services.Configure<PaperbaseAIOptions>(opt =>
        {
            opt.QaTopKChunks = 5;
        });
    }
}

public class DocumentQaAppService_Tests : PaperbaseApplicationTestBase<DocumentQaAppServiceTestModule>
{
    private readonly IDocumentQaAppService _qaAppService;
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly DocumentQaWorkflow _qaWorkflow;
    private readonly DocumentRerankWorkflow _rerankWorkflow;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public DocumentQaAppService_Tests()
    {
        _qaAppService = GetRequiredService<IDocumentQaAppService>();
        _vectorStore = GetRequiredService<IDocumentKnowledgeIndex>();
        _qaWorkflow = GetRequiredService<DocumentQaWorkflow>();
        _rerankWorkflow = GetRequiredService<DocumentRerankWorkflow>();
        _embeddingGenerator = GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        SetupDefaultEmbedding();
    }

    [Fact]
    public async Task GlobalAsk_Returns_NoRelevant_When_No_Chunks_Found()
    {
        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        var result = await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "この合同の有効期限はいつですか？"
        });

        result.ShouldNotBeNull();
        await _qaWorkflow.DidNotReceive()
            .RunRagAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QaChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_Delegates_To_Workflow_When_Chunks_Found()
    {
        var fakeResults = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = "合同期間は2026年4月から2027年3月まで。", Score = 0.95 },
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 1, Text = "契約金額は1,200,000円（税別）。", Score = 0.90 }
        };

        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(fakeResults);

        var outcome = new DocumentQaOutcome
        {
            Answer = "有効期限は2027年3月31日です。",
            ActualMode = QaMode.Rag
        };
        outcome.Sources.Add(new QaSourceItem
        {
            Text = "合同期間は2026年4月から2027年3月まで。",
            ChunkIndex = 0
        });

        _qaWorkflow
            .RunRagAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QaChunk>>(), Arg.Any<CancellationToken>())
            .Returns(outcome);

        var result = await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "この合同の有効期限はいつですか？"
        });

        result.ShouldNotBeNull();
        result.Answer.ShouldBe("有効期限は2027年3月31日です。");
        result.ActualMode.ShouldBe(QaMode.Rag);
        result.IsDegraded.ShouldBeFalse();
        result.Sources.Count.ShouldBe(1);
        result.Sources[0].ChunkIndex.ShouldBe(0);
    }

    [Fact]
    public async Task GlobalAsk_Passes_DocumentTypeCode_To_VectorSearch()
    {
        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "質問",
            DocumentTypeCode = "contract.general"
        });

        await _vectorStore.Received(1)
            .SearchAsync(
                Arg.Is<VectorSearchRequest>(r => r.DocumentTypeCode == "contract.general"),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_Forwards_Question_As_QueryText_For_Hybrid_Routing()
    {
        // QueryText is the routing signal that lets the Qdrant provider take the
        // hybrid (BM25 + dense, RRF) branch. Without it, EnableHybridSearch=true
        // is silently ineffective on the QA path.
        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        var question = "この合同の有効期限はいつですか？";
        await _qaAppService.GlobalAskAsync(new GlobalAskInput { Question = question });

        await _vectorStore.Received(1)
            .SearchAsync(
                Arg.Is<VectorSearchRequest>(r => r.QueryText == question),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_Hybrid_Results_With_Null_Score_Bypass_QaMinScore()
    {
        // When the provider returns hybrid (RRF) results, Score is null. The QA
        // app service must treat null as "no normalized score available" and let
        // those chunks through, otherwise enabling hybrid search would silently
        // drop every result on the QA path.
        var hybridResults = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = "合同期間は2026年4月から2027年3月まで。", Score = null },
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 1, Text = "契約金額は1,200,000円。", Score = null }
        };

        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(hybridResults);

        var outcome = new DocumentQaOutcome { Answer = "ans", ActualMode = QaMode.Rag };
        _qaWorkflow
            .RunRagAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QaChunk>>(), Arg.Any<CancellationToken>())
            .Returns(outcome);

        await _qaAppService.GlobalAskAsync(new GlobalAskInput { Question = "問題" });

        await _qaWorkflow.Received(1)
            .RunRagAsync(
                Arg.Any<string>(),
                Arg.Is<IReadOnlyList<QaChunk>>(c => c.Count == 2),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_Filters_Chunks_Below_QaMinScore()
    {
        // 默认 QaMinScore = 0.65；构造一组结果只有"远低于阈值"的低相似度 chunk。
        // 期望 AppService 视同空结果，不调用 RunRagAsync，并返回 NoRelevant 兜底答案。
        var lowScoreResults = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = "完全无关的内容片段。", Score = 0.05 },
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 1, Text = "另一段无关内容。", Score = 0.10 }
        };

        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(lowScoreResults);

        var result = await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "完全不相关的问题"
        });

        result.ShouldNotBeNull();
        await _qaWorkflow.DidNotReceive()
            .RunRagAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QaChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_Without_Rerank_Does_Not_Invoke_Rerank_Workflow()
    {
        var fakeResults = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = "高度相关。", Score = 0.90 }
        };
        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(fakeResults);

        _qaWorkflow
            .RunRagAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<QaChunk>>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentQaOutcome { Answer = "ans", ActualMode = QaMode.Rag });

        await _qaAppService.GlobalAskAsync(new GlobalAskInput { Question = "问题" });

        await _rerankWorkflow.DidNotReceive().RerankAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<RerankCandidate>>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GlobalAsk_With_Rerank_Enabled_Invokes_Rerank_And_Uses_Its_Order()
    {
        var rerankOptions = GetRequiredService<IOptions<PaperbaseAIOptions>>().Value;
        rerankOptions.EnableLlmRerank = true;
        rerankOptions.RecallExpandFactor = 4;

        try
        {
            // 召回 6 个高分 chunks，rerank 取前 5。
            var fakeResults = Enumerable.Range(0, 6)
                .Select(i => new VectorSearchResult
                {
                    RecordId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    ChunkIndex = i,
                    Text = $"chunk-{i}",
                    Score = 0.95 - i * 0.01
                })
                .ToList();

            _vectorStore
                .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
                .Returns(fakeResults);

            // mock rerank：把第 5 个候选（id=5）作为最高分；返回的 RerankedChunk 应保留其 ChunkText
            _rerankWorkflow
                .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RerankCandidate>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var candidates = ci.Arg<IReadOnlyList<RerankCandidate>>();
                    var top = candidates[^1]; // 把最后一个（chunk-5）排到最前
                    return (IReadOnlyList<RerankedChunk>)
                    [
                        new(top, score: 1.0, originalIndex: candidates.Count - 1),
                        new(candidates[0], score: 0.9, originalIndex: 0),
                    ];
                });

            IReadOnlyList<QaChunk>? capturedChunks = null;
            _qaWorkflow
                .RunRagAsync(Arg.Any<string>(), Arg.Do<IReadOnlyList<QaChunk>>(c => capturedChunks = c), Arg.Any<CancellationToken>())
                .Returns(new DocumentQaOutcome { Answer = "ans", ActualMode = QaMode.Rag });

            await _qaAppService.GlobalAskAsync(new GlobalAskInput { Question = "问题" });

            await _rerankWorkflow.Received(1).RerankAsync(
                Arg.Any<string>(),
                Arg.Is<IReadOnlyList<RerankCandidate>>(c => c.Count == 6),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());

            capturedChunks.ShouldNotBeNull();
            capturedChunks!.Count.ShouldBe(2);
            capturedChunks[0].ChunkIndex.ShouldBe(5); // rerank 把 chunk-5 排到最前
        }
        finally
        {
            rerankOptions.EnableLlmRerank = false;
        }
    }

    [Fact]
    public async Task GlobalAsk_Keeps_Chunks_Above_QaMinScore()
    {
        // 一半高分 / 一半低分；预期只有高分 chunks 进入 RunRagAsync
        var mixed = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = "高度相关。", Score = 0.90 },
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 1, Text = "毫不相关。", Score = 0.05 }
        };

        _vectorStore
            .SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(mixed);

        IReadOnlyList<QaChunk>? capturedChunks = null;
        _qaWorkflow
            .RunRagAsync(Arg.Any<string>(), Arg.Do<IReadOnlyList<QaChunk>>(c => capturedChunks = c), Arg.Any<CancellationToken>())
            .Returns(new DocumentQaOutcome { Answer = "ans", ActualMode = QaMode.Rag });

        await _qaAppService.GlobalAskAsync(new GlobalAskInput { Question = "问题" });

        capturedChunks.ShouldNotBeNull();
        capturedChunks!.Count.ShouldBe(1);
        capturedChunks[0].ChunkIndex.ShouldBe(0);
    }

    private void SetupDefaultEmbedding()
    {
        var vector = new float[] { 0.1f, 0.2f, 0.3f };
        var embedding = new Embedding<float>(vector);
        var embeddings = new GeneratedEmbeddings<Embedding<float>>([embedding]);

        _embeddingGenerator
            .GenerateAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(embeddings);
    }

}
