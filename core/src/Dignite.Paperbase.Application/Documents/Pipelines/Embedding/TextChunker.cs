using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dignite.Paperbase.Ai;
using Markdig;
using Markdig.Syntax;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.Pipelines.Embedding;

public class TextChunker : ITransientDependency
{
    private readonly PaperbaseAIOptions _options;
    private static readonly MarkdownPipeline MarkdigPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseFootnotes()
        .UseEmphasisExtras()
        .Build();

    public TextChunker(IOptions<PaperbaseAIOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 字符级降级路径：当上游没有 Markdown 输出（OCR Provider 当前实现）时使用。
    /// </summary>
    public virtual IReadOnlyList<string> Chunk(string text)
    {
        return ChunkPlainText(text);
    }

    /// <summary>
    /// Markdown-aware 路径。<paramref name="markdown"/> 非空时按 Markdown AST 顶层 Block
    /// （标题/段落/列表/表格/代码块）切分，并把 header path 注入到每个 chunk 头部；
    /// 单个 Block 超过 ChunkSize 时回退到字符级二次切分。
    /// <paramref name="markdown"/> 为空时直接走 <paramref name="fallbackText"/> 的字符级路径。
    /// </summary>
    public virtual IReadOnlyList<string> Chunk(string? markdown, string fallbackText)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return ChunkPlainText(fallbackText);

        return ChunkMarkdown(markdown!);
    }

    protected virtual IReadOnlyList<string> ChunkMarkdown(string markdown)
    {
        var chunkSize = Math.Max(1, _options.ChunkSize);

        var doc = Markdown.Parse(markdown, MarkdigPipeline);
        var headerStack = new List<string?>(new string?[6]); // H1..H6

        var results = new List<string>();
        var buffer = new StringBuilder();
        string? bufferHeaderPrefix = null;

        void Flush()
        {
            if (buffer.Length == 0) return;

            var body = buffer.ToString().TrimEnd();
            if (body.Length == 0) { buffer.Clear(); return; }

            results.Add(string.IsNullOrEmpty(bufferHeaderPrefix)
                ? body
                : bufferHeaderPrefix + "\n\n" + body);
            buffer.Clear();
        }

        foreach (var block in doc)
        {
            if (block is HeadingBlock heading)
            {
                // 标题边界：先冲掉缓冲，再更新 header stack，并把"标题行"作为下一个 chunk 的前缀
                Flush();

                var level = Math.Clamp(heading.Level, 1, 6);
                var titleText = ExtractInlineText(heading);

                for (var i = level - 1; i < headerStack.Count; i++)
                    headerStack[i] = null;
                headerStack[level - 1] = titleText;

                bufferHeaderPrefix = BuildHeaderPrefix(headerStack);

                // 把标题本身也放进 buffer，让 chunk 文本里能直接看到 "# Title"
                var headingMarkdown = SliceOriginal(markdown, heading);
                if (!string.IsNullOrWhiteSpace(headingMarkdown))
                    buffer.AppendLine(headingMarkdown.TrimEnd()).AppendLine();
                continue;
            }

            var blockMarkdown = SliceOriginal(markdown, block).TrimEnd();
            if (blockMarkdown.Length == 0) continue;

            // 单个 block 自己就超过 chunkSize：先 flush 当前累积，再用字符级算法把这个 block 拆碎，
            // 每个子 chunk 都带上 header path 前缀。
            if (blockMarkdown.Length > chunkSize)
            {
                Flush();

                foreach (var part in ChunkPlainText(blockMarkdown))
                {
                    results.Add(string.IsNullOrEmpty(bufferHeaderPrefix)
                        ? part
                        : bufferHeaderPrefix + "\n\n" + part);
                }
                continue;
            }

            // 累积接近 chunkSize 时 flush
            if (buffer.Length > 0 && buffer.Length + blockMarkdown.Length + 2 > chunkSize)
                Flush();

            if (buffer.Length > 0) buffer.AppendLine();
            buffer.AppendLine(blockMarkdown);
        }

        Flush();
        return results;
    }

    protected virtual IReadOnlyList<string> ChunkPlainText(string text)
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

    protected virtual string SliceOriginal(string markdown, Block block)
    {
        var span = block.Span;
        if (span.IsEmpty || span.Start < 0 || span.End >= markdown.Length || span.Length <= 0)
            return string.Empty;

        return markdown.Substring(span.Start, span.Length);
    }

    protected virtual string ExtractInlineText(LeafBlock leaf)
    {
        if (leaf.Inline == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var descendant in leaf.Inline.Descendants())
        {
            if (descendant is Markdig.Syntax.Inlines.LiteralInline lit)
                sb.Append(lit.Content.AsSpan());
            else if (descendant is Markdig.Syntax.Inlines.CodeInline code)
                sb.Append(code.Content);
        }
        return sb.ToString().Trim();
    }

    protected virtual string? BuildHeaderPrefix(IReadOnlyList<string?> headerStack)
    {
        var parts = new List<string>(headerStack.Count);
        for (var i = 0; i < headerStack.Count; i++)
        {
            var h = headerStack[i];
            if (string.IsNullOrWhiteSpace(h)) continue;
            parts.Add($"{new string('#', i + 1)} {h}");
        }

        if (parts.Count == 0) return null;
        return "> " + string.Join(" > ", parts);
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
