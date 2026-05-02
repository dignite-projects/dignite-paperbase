using System.Collections.Generic;
using Dignite.Paperbase.KnowledgeIndex;
using Dignite.Paperbase.KnowledgeIndex.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

/// <summary>
/// 守护"PaperbaseKnowledgeIndexOptions.EmbeddingDimension 必须等于 QdrantKnowledgeIndexOptions.VectorDimension"
/// 这一启动期不变量。配置不一致时，必须在第一次解析 Options 时抛
/// <see cref="OptionsValidationException"/>，让宿主无法静默运行在错配的 collection 上。
///
/// 注：仅覆盖维度对齐校验，不涵盖 QdrantKnowledgeIndexOptions 的其他约束（Endpoint 非空、
/// CollectionName 非空、VectorDimension &gt; 0 等）。
/// </summary>
public class PaperbaseKnowledgeIndexOptionsValidationTests
{
    [Fact]
    public void Default_Dimension_Matches_Qdrant_Default()
    {
        var kixOptions = new PaperbaseKnowledgeIndexOptions();
        var qdrantOptions = new QdrantKnowledgeIndexOptions();
        kixOptions.EmbeddingDimension.ShouldBe(qdrantOptions.VectorDimension);
    }

    [Fact]
    public void Mismatched_Dimension_Throws_OptionsValidationException_On_First_Access()
    {
        var services = new ServiceCollection();
        services.AddOptions<QdrantKnowledgeIndexOptions>()
            .Configure(o => o.VectorDimension = new QdrantKnowledgeIndexOptions().VectorDimension);
        services.AddOptions<PaperbaseKnowledgeIndexOptions>()
            .Configure(o => o.EmbeddingDimension = new QdrantKnowledgeIndexOptions().VectorDimension + 1)
            .Validate<IOptions<QdrantKnowledgeIndexOptions>>(
                (kix, qdrant) => kix.EmbeddingDimension == qdrant.Value.VectorDimension,
                "Embedding dimension mismatch");

        var sp = services.BuildServiceProvider();

        var ex = Should.Throw<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<PaperbaseKnowledgeIndexOptions>>().Value);

        ex.Failures.ShouldContain(f => f.Contains("Embedding dimension mismatch"));
    }

    [Fact]
    public void Matching_Dimension_Resolves_Successfully()
    {
        var services = new ServiceCollection();
        services.AddOptions<QdrantKnowledgeIndexOptions>()
            .Configure(o => o.VectorDimension = new QdrantKnowledgeIndexOptions().VectorDimension);
        services.AddOptions<PaperbaseKnowledgeIndexOptions>()
            .Configure(o => o.EmbeddingDimension = new QdrantKnowledgeIndexOptions().VectorDimension)
            .Validate<IOptions<QdrantKnowledgeIndexOptions>>(
                (kix, qdrant) => kix.EmbeddingDimension == qdrant.Value.VectorDimension,
                "Embedding dimension mismatch");

        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<IOptions<PaperbaseKnowledgeIndexOptions>>().Value;
        resolved.EmbeddingDimension.ShouldBe(new QdrantKnowledgeIndexOptions().VectorDimension);
    }

    [Fact]
    public void PaperbaseKnowledgeIndexOptions_Binds_From_Configuration_Section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaperbaseKnowledgeIndex:EmbeddingDimension"] = "3072",
                ["PaperbaseKnowledgeIndex:DefaultTopK"] = "9",
                ["PaperbaseKnowledgeIndex:MinScore"] = "0.42",
                ["QdrantKnowledgeIndex:VectorDimension"] = "3072"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<QdrantKnowledgeIndexOptions>()
            .BindConfiguration("QdrantKnowledgeIndex");

        var context = new ServiceConfigurationContext(services);
        new PaperbaseKnowledgeIndexModule().ConfigureServices(context);
        new QdrantKnowledgeIndexModule().PostConfigureServices(context);

        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<IOptions<PaperbaseKnowledgeIndexOptions>>().Value;
        resolved.EmbeddingDimension.ShouldBe(3072);
        resolved.DefaultTopK.ShouldBe(9);
        resolved.MinScore.ShouldBe(0.42);
    }
}
