using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

/// <summary>
/// Client-side TF encoder for Qdrant's BM25 sparse vectors.
/// Token IDs are FNV-1a hashes of lowercase terms — deterministic across
/// .NET versions and consistent between indexing and query time without
/// requiring a shared vocabulary table.
/// Qdrant applies IDF normalization at search time via <see cref="Qdrant.Client.Grpc.Modifier.Idf"/>.
/// </summary>
internal static class SparseBm25Encoder
{
    internal static (float[] Values, uint[] Indices) Encode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (Array.Empty<float>(), Array.Empty<uint>());

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return (Array.Empty<float>(), Array.Empty<uint>());

        var termFreq = new Dictionary<uint, int>(tokens.Count);
        foreach (var token in tokens)
        {
            var hash = FnvHash(token);
            termFreq.TryGetValue(hash, out int freq);
            termFreq[hash] = freq + 1;
        }

        float total = tokens.Count;
        var indices = termFreq.Keys.ToArray();
        var values = termFreq.Values.Select(f => f / total).ToArray();
        return (values, indices);
    }

    private static List<string> Tokenize(string text)
        => Regex.Split(text.ToLowerInvariant(), @"[^\w]+")
               .Where(t => t.Length >= 2)
               .ToList();

    private static uint FnvHash(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
        {
            hash ^= (uint)c;
            hash *= 16777619u;
        }
        return hash;
    }
}
