using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Rag.Benchmarks;

public class ProductionBenchmarkDatasetTests
{
    [Fact]
    public void Validate_Should_Fail_When_Embeddings_Are_Empty()
    {
        var dataset = CreateDataset(
            chunks:
            [
                new ProductionChunk
                {
                    Id = TestChunkId,
                    Text = "chunk",
                    DocumentTypeCode = "contract.general",
                    EmbeddingBase64 = string.Empty
                }
            ]);

        var exception = Should.Throw<InvalidOperationException>(() => dataset.Validate());

        exception.Message.ShouldContain("empty embedding");
    }

    [Fact]
    public void Validate_Should_Fail_When_Dimension_Does_Not_Match_Schema()
    {
        var dataset = CreateDataset(
            embeddingDimension: ProductionBenchmarkDataset.ExpectedEmbeddingDimension + 1);

        var exception = Should.Throw<InvalidOperationException>(() => dataset.Validate());

        exception.Message.ShouldContain("embeddingDimension");
    }

    [Fact]
    public void Validate_Should_Fail_When_Required_Query_Category_Is_Missing()
    {
        var dataset = CreateDataset(
            queries:
            [
                CreateQuery("q1", "precise-text")
            ]);

        var exception = Should.Throw<InvalidOperationException>(() => dataset.Validate());

        exception.Message.ShouldContain("semantic");
    }

    [Fact]
    public void Validate_Should_Fail_When_ExpectedChunkId_Is_Missing()
    {
        var dataset = CreateDataset(
            queries:
            [
                CreateQuery("q1", "precise-text", Guid.NewGuid()),
                CreateQuery("q2", "semantic")
            ]);

        var exception = Should.Throw<InvalidOperationException>(() => dataset.Validate());

        exception.Message.ShouldContain("missing expectedChunkId");
    }

    private static readonly Guid TestChunkId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static ProductionBenchmarkDataset CreateDataset(
        int? embeddingDimension = null,
        IReadOnlyList<ProductionChunk>? chunks = null,
        IReadOnlyList<ProductionQuery>? queries = null)
    {
        return new ProductionBenchmarkDataset
        {
            Version = "test",
            EmbeddingDimension = embeddingDimension ?? ProductionBenchmarkDataset.ExpectedEmbeddingDimension,
            Chunks = chunks ??
            [
                new ProductionChunk
                {
                    Id = TestChunkId,
                    Text = "chunk",
                    DocumentTypeCode = "contract.general",
                    EmbeddingBase64 = EncodeVector(0.1f)
                }
            ],
            Queries = queries ??
            [
                CreateQuery("q1", "precise-text"),
                CreateQuery("q2", "semantic")
            ]
        };
    }

    private static ProductionQuery CreateQuery(
        string id,
        string category,
        Guid? expectedChunkId = null)
    {
        return new ProductionQuery
        {
            Id = id,
            Text = "query",
            Category = category,
            ExpectedChunkIds = [expectedChunkId ?? TestChunkId],
            EmbeddingBase64 = EncodeVector(0.2f)
        };
    }

    private static string EncodeVector(float firstValue)
    {
        var floats = new float[ProductionBenchmarkDataset.ExpectedEmbeddingDimension];
        floats[0] = firstValue;
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }
}
