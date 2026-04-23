using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.AI;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Documents;

// ────────────────────────────────────────────────────────────────────────────
// Test module：注册三个 mock 替换真实 AI 依赖
// ────────────────────────────────────────────────────────────────────────────

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentQaAppServiceTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(
            Substitute.For<IDocumentChunkRepository>());

        context.Services.AddSingleton(
            Substitute.For<IQaService>());

        context.Services.AddSingleton(
            Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>());

        context.Services.Configure<PaperbaseAIOptions>(opt =>
        {
            opt.QaTopKChunks = 5;
        });
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Tests
// ────────────────────────────────────────────────────────────────────────────

public class DocumentQaAppService_Tests : PaperbaseApplicationTestBase<DocumentQaAppServiceTestModule>
{
    private readonly IDocumentQaAppService _qaAppService;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IQaService _qaService;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public DocumentQaAppService_Tests()
    {
        _qaAppService = GetRequiredService<IDocumentQaAppService>();
        _chunkRepository = GetRequiredService<IDocumentChunkRepository>();
        _qaService = GetRequiredService<IQaService>();
        _embeddingGenerator = GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        SetupDefaultEmbedding();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 1: chunk 检索返回空 → 返回「无相关文档」提示
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalAsk_Returns_NoRelevant_When_No_Chunks_Found()
    {
        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                documentId: null, documentTypeCode: null,
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        var result = await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "この合同の有効期限はいつですか？"
        });

        result.ShouldNotBeNull();
        // IQaService は呼ばれない
        await _qaService.DidNotReceive()
            .AskAsync(Arg.Any<QaRequest>(), Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 2: 有 chunks → 调用 IQaService 并映射返回值
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalAsk_Delegates_To_QaService_When_Chunks_Found()
    {
        var fakeChunks = new List<DocumentChunk>
        {
            CreateFakeChunk(Guid.NewGuid(), 0, "合同期間は2026年4月から2027年3月まで。"),
            CreateFakeChunk(Guid.NewGuid(), 1, "契約金額は1,200,000円（税別）。")
        };

        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                documentId: null, documentTypeCode: null,
                Arg.Any<CancellationToken>())
            .Returns(fakeChunks);

        _qaService
            .AskAsync(Arg.Any<QaRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QaResult
            {
                Answer = "有効期限は2027年3月31日です。",
                ActualMode = QaMode.Rag,
                Sources = new List<QaSource>
                {
                    new() { Text = "合同期間は2026年4月から2027年3月まで。", ChunkIndex = 0 }
                }
            });

        var result = await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "この合同の有効期限はいつですか？"
        });

        result.ShouldNotBeNull();
        result.Answer.ShouldBe("有効期限は2027年3月31日です。");
        result.ActualMode.ShouldBe(QaMode.Rag.ToString());
        result.IsDegraded.ShouldBeFalse();
        result.Sources.Count.ShouldBe(1);
        result.Sources[0].ChunkIndex.ShouldBe(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 3: DocumentTypeCode 过滤器透传给 chunk 检索
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalAsk_Passes_DocumentTypeCode_To_ChunkSearch()
    {
        _chunkRepository
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                documentId: null, documentTypeCode: Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<DocumentChunk>());

        await _qaAppService.GlobalAskAsync(new GlobalAskInput
        {
            Question = "質問",
            DocumentTypeCode = "contract.general"
        });

        await _chunkRepository.Received(1)
            .SearchByVectorAsync(
                Arg.Any<float[]>(), Arg.Any<int>(),
                documentId: null, documentTypeCode: "contract.general",
                Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

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

    private static DocumentChunk CreateFakeChunk(Guid documentId, int chunkIndex, string text)
    {
        var chunk = new DocumentChunk(
            Guid.NewGuid(), null, documentId, chunkIndex, text,
            new float[] { 0.1f, 0.2f, 0.3f });
        return chunk;
    }
}
