using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Rag;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dignite.Paperbase.Documents;

[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class DocumentTextSearchAdapterTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Adapter dependencies — replaced by NSubstitutes so the test can shape
        // the search results and assert on the embedding call.
        context.Services.AddSingleton(Substitute.For<IDocumentKnowledgeIndex>());
        context.Services.AddSingleton(Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>());
    }
}

/// <summary>
/// Slice 8 守护：<see cref="DocumentTextSearchAdapter"/> 让 Microsoft Agent Framework 的
/// <see cref="TextSearchProvider"/> 能复用 Paperbase 的 <see cref="IDocumentKnowledgeIndex"/>。
/// 这些测试覆盖：citation 字段映射、按 Mode 跳过/触发 embedding、scope 覆盖默认配置、
/// 多租户 TenantId 显式传递。
/// </summary>
public class DocumentTextSearchAdapter_Tests
    : PaperbaseApplicationTestBase<DocumentTextSearchAdapterTestModule>
{
    private readonly DocumentTextSearchAdapter _adapter;
    private readonly IDocumentKnowledgeIndex _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public DocumentTextSearchAdapter_Tests()
    {
        _adapter = GetRequiredService<DocumentTextSearchAdapter>();
        _vectorStore = GetRequiredService<IDocumentKnowledgeIndex>();
        _embeddingGenerator = GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        SetupDefaultEmbedding();
    }

    [Fact]
    public void CreateForTenant_Returns_TextSearchProvider_That_Wraps_Adapter()
    {
        // 烟雾测试：构造能产出非 null 的 TextSearchProvider；
        // 行为细节通过 SearchAsync 的直调测试覆盖。
        var provider = _adapter.CreateForTenant(tenantId: Guid.NewGuid());
        provider.ShouldNotBeNull();
    }

    [Fact]
    public async Task SearchAsync_Forwards_TenantId_And_QueryText_To_VectorStore()
    {
        var tenantId = Guid.NewGuid();
        VectorSearchRequest? captured = null;
        _vectorStore.SearchAsync(
                Arg.Do<VectorSearchRequest>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _adapter.SearchAsync(tenantId, scope: null, query: "契約番号 ABC-001");

        captured.ShouldNotBeNull();
        captured!.TenantId.ShouldBe(tenantId);
        captured.QueryText.ShouldBe("契約番号 ABC-001");
    }

    [Fact]
    public async Task Vector_Mode_Generates_Embedding_For_Query()
    {
        // Vector / Hybrid 模式必须把 query 喂给 embedding 生成器；TextSearchProvider
        // 只给 string，adapter 负责补齐向量。
        var scope = new DocumentSearchScope { Mode = VectorSearchMode.Vector };
        _vectorStore.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _adapter.SearchAsync(tenantId: null, scope, query: "ANYTHING");

        await _embeddingGenerator.Received(1).GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Keyword_Mode_Skips_Embedding_Generation()
    {
        // Keyword 模式不需要向量；跳过 embedding 调用是显式优化。
        // 这条测试如果挂了，说明 adapter 在 Keyword 模式下也调了 embedding 服务，
        // 那是不必要的成本 / 延迟。
        var scope = new DocumentSearchScope { Mode = VectorSearchMode.Keyword };
        _vectorStore.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _adapter.SearchAsync(tenantId: null, scope, query: "ANYTHING");

        await _embeddingGenerator.DidNotReceive().GenerateAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<EmbeddingGenerationOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scope_Overrides_DefaultTopK_DocumentId_DocumentTypeCode_MinScore()
    {
        var documentId = Guid.NewGuid();
        var scope = new DocumentSearchScope
        {
            DocumentId = documentId,
            DocumentTypeCode = "contract.general",
            TopK = 17,
            MinScore = 0.42,
            Mode = VectorSearchMode.Hybrid
        };

        VectorSearchRequest? captured = null;
        _vectorStore.SearchAsync(Arg.Do<VectorSearchRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _adapter.SearchAsync(tenantId: null, scope, query: "Q");

        captured.ShouldNotBeNull();
        captured!.DocumentId.ShouldBe(documentId);
        captured.DocumentTypeCode.ShouldBe("contract.general");
        captured.TopK.ShouldBe(17);
        captured.MinScore.ShouldBe(0.42);
        captured.Mode.ShouldBe(VectorSearchMode.Hybrid);
    }

    [Fact]
    public async Task Result_Title_Maps_To_TextSearchResult_SourceName()
    {
        // citation 主路径：Title 优先作为 SourceName，让 LLM 在回答里直接引用。
        _vectorStore.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>
            {
                new()
                {
                    RecordId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    ChunkIndex = 0,
                    Text = "契約期間: 2026-04-01 から 2027-03-31",
                    Title = "§3.1 期間条項",
                    PageNumber = 4
                }
            });

        var results = (await _adapter.SearchAsync(tenantId: null, scope: null, query: "Q")).ToList();

        results.Count.ShouldBe(1);
        results[0].SourceName.ShouldBe("§3.1 期間条項");
        results[0].Text.ShouldBe("契約期間: 2026-04-01 から 2027-03-31");
    }

    [Fact]
    public async Task Result_Without_Title_But_With_PageNumber_Falls_Back_To_Page_Format()
    {
        var documentId = Guid.NewGuid();
        _vectorStore.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>
            {
                new()
                {
                    RecordId = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkIndex = 5,
                    Text = "...",
                    Title = null,
                    PageNumber = 12
                }
            });

        var results = (await _adapter.SearchAsync(tenantId: null, scope: null, query: "Q")).ToList();

        results[0].SourceName.ShouldBe($"Document {documentId} (page 12)");
    }

    [Fact]
    public async Task Result_Without_Title_Or_PageNumber_Falls_Back_To_Chunk_Index_Format()
    {
        var documentId = Guid.NewGuid();
        _vectorStore.SearchAsync(Arg.Any<VectorSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>
            {
                new()
                {
                    RecordId = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkIndex = 7,
                    Text = "...",
                    Title = null,
                    PageNumber = null
                }
            });

        var results = (await _adapter.SearchAsync(tenantId: null, scope: null, query: "Q")).ToList();

        results[0].SourceName.ShouldBe($"Document {documentId} (chunk #7)");
    }

    [Fact]
    public async Task Default_Mode_Comes_From_PaperbaseRagOptions()
    {
        // PaperbaseRagOptions.DefaultSearchMode 是切到 Hybrid 的唯一开关。
        // adapter 必须读这个值，不能硬编码。
        var ragOptions = GetRequiredService<IOptions<PaperbaseRagOptions>>().Value;
        var original = ragOptions.DefaultSearchMode;
        ragOptions.DefaultSearchMode = VectorSearchMode.Hybrid;

        try
        {
            VectorSearchRequest? captured = null;
            _vectorStore.SearchAsync(Arg.Do<VectorSearchRequest>(r => captured = r), Arg.Any<CancellationToken>())
                .Returns(new List<VectorSearchResult>());

            await _adapter.SearchAsync(tenantId: null, scope: null, query: "Q");

            captured.ShouldNotBeNull();
            captured!.Mode.ShouldBe(VectorSearchMode.Hybrid);
        }
        finally
        {
            ragOptions.DefaultSearchMode = original;
        }
    }

    [Fact]
    public async Task Different_Tenants_Get_Different_TenantId_In_Search_Request()
    {
        // 多租户隔离守护：连续搜两个不同租户，request.TenantId 必须严格匹配传入值。
        // 类似 embedding job 的多租户守护——adapter 不依赖 ambient context。
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var captured = new List<VectorSearchRequest>();
        _vectorStore.SearchAsync(Arg.Do<VectorSearchRequest>(r => captured.Add(r)), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await _adapter.SearchAsync(tenantA, scope: null, query: "A");
        await _adapter.SearchAsync(tenantB, scope: null, query: "B");

        captured.Count.ShouldBe(2);
        captured[0].TenantId.ShouldBe(tenantA);
        captured[1].TenantId.ShouldBe(tenantB);
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
