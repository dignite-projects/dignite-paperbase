using Dignite.Paperbase.Rag.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Rag.Qdrant;

/// <summary>
/// 守护"PaperbaseRagOptions.EmbeddingDimension 必须等于 QdrantRagOptions.VectorDimension"
/// 这一启动期不变量。配置不一致时，必须在第一次解析 Options 时抛
/// <see cref="OptionsValidationException"/>，让宿主无法静默运行在错配的 collection 上。
///
/// 注：仅覆盖维度对齐校验，不涵盖 QdrantRagOptions 的其他约束（Endpoint 非空、
/// CollectionName 非空、VectorDimension &gt; 0 等）。
/// </summary>
public class PaperbaseRagOptionsValidationTests
{
    [Fact]
    public void Default_Dimension_Matches_Qdrant_Default()
    {
        var ragOptions = new PaperbaseRagOptions();
        var qdrantOptions = new QdrantRagOptions();
        ragOptions.EmbeddingDimension.ShouldBe(qdrantOptions.VectorDimension);
    }

    [Fact]
    public void Mismatched_Dimension_Throws_OptionsValidationException_On_First_Access()
    {
        var services = new ServiceCollection();
        services.AddOptions<QdrantRagOptions>()
            .Configure(o => o.VectorDimension = new QdrantRagOptions().VectorDimension);
        services.AddOptions<PaperbaseRagOptions>()
            .Configure(o => o.EmbeddingDimension = new QdrantRagOptions().VectorDimension + 1)
            .Validate<IOptions<QdrantRagOptions>>(
                (rag, qdrant) => rag.EmbeddingDimension == qdrant.Value.VectorDimension,
                "Embedding dimension mismatch");

        var sp = services.BuildServiceProvider();

        var ex = Should.Throw<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<PaperbaseRagOptions>>().Value);

        ex.Failures.ShouldContain(f => f.Contains("Embedding dimension mismatch"));
    }

    [Fact]
    public void Matching_Dimension_Resolves_Successfully()
    {
        var services = new ServiceCollection();
        services.AddOptions<QdrantRagOptions>()
            .Configure(o => o.VectorDimension = new QdrantRagOptions().VectorDimension);
        services.AddOptions<PaperbaseRagOptions>()
            .Configure(o => o.EmbeddingDimension = new QdrantRagOptions().VectorDimension)
            .Validate<IOptions<QdrantRagOptions>>(
                (rag, qdrant) => rag.EmbeddingDimension == qdrant.Value.VectorDimension,
                "Embedding dimension mismatch");

        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<IOptions<PaperbaseRagOptions>>().Value;
        resolved.EmbeddingDimension.ShouldBe(new QdrantRagOptions().VectorDimension);
    }
}
