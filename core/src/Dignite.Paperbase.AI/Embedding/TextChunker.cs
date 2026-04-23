using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.AI.Embedding;

public class TextChunker : ITransientDependency
{
    private readonly PaperbaseAIOptions _options;

    public TextChunker(IOptions<PaperbaseAIOptions> options)
    {
        _options = options.Value;
    }

    public virtual IReadOnlyList<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var results = new List<string>();
        var chunkSize = _options.ChunkSize;
        var step = chunkSize - _options.ChunkOverlap;
        if (step <= 0) step = chunkSize;

        var i = 0;
        while (i < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - i);
            results.Add(text.Substring(i, length));
            if (i + chunkSize >= text.Length) break;
            i += step;
        }

        return results;
    }
}
