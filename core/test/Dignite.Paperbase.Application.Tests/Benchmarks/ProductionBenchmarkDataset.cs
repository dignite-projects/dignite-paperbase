using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Loads the desensitized gold dataset used by <see cref="ProductionHybridSearchBenchmark"/>.
/// The dataset JSON is NOT committed to the repo (contains desensitized real data).
/// See <c>core/test/Dignite.Paperbase.Application.Tests/Benchmarks/README.md</c> for preparation instructions and
/// <c>core/test/Dignite.Paperbase.Application.Tests/Benchmarks/rag-gold-dataset-sample.json</c> for the expected schema.
/// </summary>
public sealed class ProductionBenchmarkDataset
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = default!;

    [JsonPropertyName("embeddingDimension")]
    public int EmbeddingDimension { get; init; }

    [JsonPropertyName("chunks")]
    public IReadOnlyList<ProductionChunk> Chunks { get; init; } = default!;

    [JsonPropertyName("queries")]
    public IReadOnlyList<ProductionQuery> Queries { get; init; } = default!;

    public static ProductionBenchmarkDataset Load(string path)
    {
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<ProductionBenchmarkDataset>(json, opts)
               ?? throw new InvalidOperationException($"Failed to deserialize dataset at {path}");
    }

    public void Validate()
    {
        if (Chunks is null)
        {
            throw new InvalidOperationException("Dataset chunks must be present.");
        }

        if (Queries is null)
        {
            throw new InvalidOperationException("Dataset queries must be present.");
        }

        if (EmbeddingDimension != PaperbaseDbProperties.EmbeddingVectorDimension)
        {
            throw new InvalidOperationException(
                $"Dataset embeddingDimension {EmbeddingDimension} does not match " +
                $"{nameof(PaperbaseDbProperties.EmbeddingVectorDimension)} " +
                $"{PaperbaseDbProperties.EmbeddingVectorDimension}.");
        }

        if (Chunks.Count == 0)
        {
            throw new InvalidOperationException("Dataset must contain at least one chunk.");
        }

        if (Queries.Count == 0)
        {
            throw new InvalidOperationException("Dataset must contain at least one query.");
        }

        ValidateCategories();
        ValidateEmbeddings();
        ValidateExpectedChunkIds();
    }

    private void ValidateCategories()
    {
        foreach (var category in new[] { "precise-text", "semantic" })
        {
            var count = Queries.Count(q => string.Equals(q.Category, category, StringComparison.Ordinal));
            if (count == 0)
            {
                throw new InvalidOperationException(
                    $"Dataset must contain at least one '{category}' query.");
            }
        }
    }

    private void ValidateEmbeddings()
    {
        foreach (var chunk in Chunks)
        {
            ValidateEmbedding(chunk.DecodeEmbedding(), $"chunk {chunk.Id}");
        }

        foreach (var query in Queries)
        {
            ValidateEmbedding(query.DecodeEmbedding(), $"query {query.Id}");
        }
    }

    private static void ValidateEmbedding(float[] embedding, string label)
    {
        if (embedding.Length == 0)
        {
            throw new InvalidOperationException($"{label} has an empty embedding.");
        }

        if (embedding.Length != PaperbaseDbProperties.EmbeddingVectorDimension)
        {
            throw new InvalidOperationException(
                $"{label} embedding length {embedding.Length} does not match " +
                $"{PaperbaseDbProperties.EmbeddingVectorDimension}.");
        }

        if (embedding.All(v => v == 0f))
        {
            throw new InvalidOperationException($"{label} has an all-zero embedding.");
        }
    }

    private void ValidateExpectedChunkIds()
    {
        var chunkIds = Chunks.Select(c => c.Id).ToHashSet();
        foreach (var query in Queries)
        {
            foreach (var expectedChunkId in query.ExpectedChunkIds)
            {
                if (!chunkIds.Contains(expectedChunkId))
                {
                    throw new InvalidOperationException(
                        $"Query {query.Id} references missing expectedChunkId {expectedChunkId}.");
                }
            }
        }
    }

    /// <summary>
    /// Walks up from the test assembly directory until it finds
    /// <c>core/test/Dignite.Paperbase.Application.Tests/Benchmarks/rag-gold-dataset.json</c> relative to the repo root.
    /// Throws if not found (the file is not committed — user must prepare it).
    /// </summary>
    public static string LocateDatasetPath()
    {
        var overrideEnv = Environment.GetEnvironmentVariable("PAPERBASE_BENCH_DATASET_PATH");
        if (!string.IsNullOrWhiteSpace(overrideEnv))
            return overrideEnv;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "core", "test", "Dignite.Paperbase.Application.Tests", "Benchmarks", "rag-gold-dataset.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        throw new FileNotFoundException(
            "core/test/Dignite.Paperbase.Application.Tests/Benchmarks/rag-gold-dataset.json not found. " +
            "Prepare the desensitized dataset per Benchmarks/README.md, " +
            "or set PAPERBASE_BENCH_DATASET_PATH to an explicit path.");
    }
}

public sealed class ProductionChunk
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = default!;

    [JsonPropertyName("documentTypeCode")]
    public string DocumentTypeCode { get; init; } = "benchmark";

    /// <summary>
    /// Base64 of the raw little-endian float32[] bytes produced by the embedding model.
    /// Generate with: <c>base64.b64encode(struct.pack(f"{N}f", *floats))</c> in Python.
    /// </summary>
    [JsonPropertyName("embeddingBase64")]
    public string EmbeddingBase64 { get; init; } = default!;

    public float[] DecodeEmbedding()
    {
        if (string.IsNullOrEmpty(EmbeddingBase64))
            return Array.Empty<float>();

        var bytes = Convert.FromBase64String(EmbeddingBase64);
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    /// <summary>Formats the embedding as a PostgreSQL vector literal: <c>[f1,f2,...]</c>.</summary>
    public string ToVectorLiteral()
    {
        var floats = DecodeEmbedding();
        return "[" + string.Join(",", floats) + "]";
    }
}

public sealed class ProductionQuery
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("text")]
    public string Text { get; init; } = default!;

    [JsonPropertyName("category")]
    public string Category { get; init; } = default!;

    [JsonPropertyName("expectedChunkIds")]
    public IReadOnlyList<Guid> ExpectedChunkIds { get; init; } = default!;

    [JsonPropertyName("embeddingBase64")]
    public string EmbeddingBase64 { get; init; } = default!;

    public float[] DecodeEmbedding()
    {
        if (string.IsNullOrEmpty(EmbeddingBase64))
            return Array.Empty<float>();

        var bytes = Convert.FromBase64String(EmbeddingBase64);
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    public string ToVectorLiteral()
    {
        var floats = DecodeEmbedding();
        return "[" + string.Join(",", floats) + "]";
    }
}
