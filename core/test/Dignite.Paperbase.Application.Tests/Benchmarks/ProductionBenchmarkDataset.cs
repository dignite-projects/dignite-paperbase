using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Loads the desensitized gold dataset used by <see cref="ProductionHybridSearchBenchmark"/>.
/// The dataset JSON is NOT committed to the repo (contains desensitized real data).
/// See <c>docs/benchmarks/README.md</c> for preparation instructions and
/// <c>docs/benchmarks/rag-gold-dataset-sample.json</c> for the expected schema.
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

    /// <summary>
    /// Walks up from the test assembly directory until it finds
    /// <c>docs/benchmarks/rag-gold-dataset.json</c> relative to the repo root.
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
            var candidate = Path.Combine(dir, "docs", "benchmarks", "rag-gold-dataset.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        throw new FileNotFoundException(
            "docs/benchmarks/rag-gold-dataset.json not found. " +
            "Prepare the desensitized dataset per docs/benchmarks/README.md, " +
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
