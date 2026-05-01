using System;
using System.Collections.Generic;
using Dignite.Paperbase.Ai;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.Embedding;

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

        var chunkSize = Math.Max(1, _options.ChunkSize);
        var overlap = Math.Max(0, Math.Min(_options.ChunkOverlap, chunkSize - 1));
        var tolerance = Math.Max(0, Math.Min(_options.ChunkBoundaryTolerance, chunkSize - 1));

        var results = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var idealEnd = Math.Min(i + chunkSize, text.Length);
            var actualEnd = idealEnd >= text.Length
                ? idealEnd
                : FindBackwardBreakPoint(text, i + 1, idealEnd, tolerance);

            results.Add(text.Substring(i, actualEnd - i));

            if (actualEnd >= text.Length) break;

            var nextStart = overlap > 0
                ? Math.Max(i + 1, actualEnd - overlap)
                : actualEnd;

            if (overlap > 0 && tolerance > 0 && nextStart < actualEnd)
            {
                var snapEnd = Math.Min(nextStart + tolerance, actualEnd);
                var snapped = FindForwardBreakPoint(text, nextStart, snapEnd);
                if (snapped > nextStart) nextStart = snapped;
            }

            if (nextStart <= i) nextStart = i + 1;
            i = nextStart;
        }

        return results;
    }

    /// <summary>
    /// 在 <c>[idealEnd - tolerance, idealEnd)</c> 范围内向后查找最近的自然断点，
    /// 返回值是切片的 <c>EndExclusive</c>（即 chunk 末尾的下一个字符位置）。
    /// 优先级：段落 (\n\n) &gt; 强句末标点 &gt; 弱句末/子句标点 &gt; idealEnd 兜底。
    /// </summary>
    protected virtual int FindBackwardBreakPoint(string text, int minIndex, int idealEnd, int tolerance)
    {
        if (idealEnd <= minIndex) return idealEnd;

        var lower = Math.Max(minIndex, idealEnd - tolerance);

        for (var k = idealEnd - 1; k > lower; k--)
        {
            if (text[k] == '\n' && text[k - 1] == '\n')
                return k + 1;
        }

        for (var k = idealEnd - 1; k >= lower; k--)
        {
            if (IsStrongSentenceEnd(text, k))
                return k + 1;
        }

        for (var k = idealEnd - 1; k >= lower; k--)
        {
            if (IsWeakBreak(text[k]))
                return k + 1;
        }

        return idealEnd;
    }

    /// <summary>
    /// 在 <c>[start, end)</c> 范围内向前查找最近的自然断点。
    /// 找到时返回断点之后的位置，否则返回 <paramref name="start"/>。
    /// </summary>
    protected virtual int FindForwardBreakPoint(string text, int start, int end)
    {
        if (end <= start) return start;

        for (var k = start; k < end - 1; k++)
        {
            if (text[k] == '\n' && text[k + 1] == '\n')
                return k + 2;
        }

        for (var k = start; k < end; k++)
        {
            if (IsStrongSentenceEnd(text, k))
                return k + 1;
        }

        for (var k = start; k < end; k++)
        {
            if (IsWeakBreak(text[k]))
                return k + 1;
        }

        return start;
    }

    private static bool IsStrongSentenceEnd(string text, int index)
    {
        var c = text[index];
        if (c == '。' || c == '！' || c == '？' || c == '．') return true;
        if (c == '.' || c == '!' || c == '?')
        {
            if (index + 1 >= text.Length) return true;
            var next = text[index + 1];
            return next == ' ' || next == '\t' || next == '\n' || next == '\r';
        }
        return false;
    }

    private static bool IsWeakBreak(char c)
    {
        return c switch
        {
            '；' or '：' or ';' or ':' => true,
            '，' or '、' or ',' => true,
            '\n' => true,
            _ => false,
        };
    }
}
