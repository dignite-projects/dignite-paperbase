using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ElBruno.MarkItDotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown;

[ExposeServices(typeof(IMarkdownTextProvider))]
public class ElBrunoMarkdownProvider : IMarkdownTextProvider, ITransientDependency
{
    private readonly MarkdownService _markdownService;

    public ILogger<ElBrunoMarkdownProvider> Logger { get; set; } = NullLogger<ElBrunoMarkdownProvider>.Instance;

    public ElBrunoMarkdownProvider(MarkdownService markdownService)
    {
        _markdownService = markdownService;
    }

    public virtual bool CanHandle(string contentType, string fileExtension)
    {
        // ElBruno 内部按扩展名 ConverterRegistry 解析，未注册时返回失败 ConversionResult。
        // 这里乐观返回 true，由 ExtractAsync 把不支持的格式转换为空文本（交由 DefaultTextExtractor 处理）。
        return !string.IsNullOrWhiteSpace(fileExtension);
    }

    public virtual async Task<MarkdownExtractionResult> ExtractAsync(
        Stream fileStream,
        MarkdownExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var conversion = await _markdownService.ConvertAsync(
            fileStream,
            context.FileExtension ?? string.Empty,
            cancellationToken);

        if (!conversion.Success)
        {
            Logger.LogDebug("ElBruno conversion failed for {Extension}: {Error}",
                context.FileExtension, conversion.ErrorMessage);
            return new MarkdownExtractionResult { Text = string.Empty, Markdown = null };
        }

        var markdown = conversion.Markdown ?? string.Empty;
        var text = StripMarkdownToPlainText(markdown);

        return new MarkdownExtractionResult
        {
            Text = text,
            Markdown = string.IsNullOrEmpty(markdown) ? null : markdown,
            PageCount = conversion.Metadata?.PageCount ?? 0,
            DetectedLanguage = null,
        };
    }

    /// <summary>
    /// 从 Markdown 反推纯文本（去除标记）。仅做语法层面的去除，不解析复杂结构。
    /// </summary>
    protected virtual string StripMarkdownToPlainText(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        var s = markdown;

        // 围栏代码块 ``` ... ``` —— 保留内部文本，去掉栅栏行
        s = Regex.Replace(s, @"^```[^\n]*\n", string.Empty, RegexOptions.Multiline);
        s = Regex.Replace(s, @"\n```\s*$", string.Empty, RegexOptions.Multiline);

        // 图片 ![alt](url) → alt
        s = Regex.Replace(s, @"!\[([^\]]*)\]\([^\)]*\)", "$1");

        // 链接 [text](url) → text
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^\)]*\)", "$1");

        // 表格分隔行 |---|---|
        s = Regex.Replace(s, @"^\s*\|?[\s:\-\|]+\|\s*$", string.Empty, RegexOptions.Multiline);

        // 表格管道符 → 空格
        s = s.Replace("|", " ");

        // 标题 # ## ### ...
        s = Regex.Replace(s, @"^\s{0,3}#{1,6}\s*", string.Empty, RegexOptions.Multiline);

        // 引用 >
        s = Regex.Replace(s, @"^\s{0,3}>\s?", string.Empty, RegexOptions.Multiline);

        // 列表项 -, *, +, 1.
        s = Regex.Replace(s, @"^\s{0,3}([-*+]|\d+\.)\s+", string.Empty, RegexOptions.Multiline);

        // 水平线 ---, ***, ___
        s = Regex.Replace(s, @"^\s{0,3}([-*_]\s*){3,}$", string.Empty, RegexOptions.Multiline);

        // 加粗/斜体 **x**, __x__, *x*, _x_
        s = Regex.Replace(s, @"(\*\*|__)(.+?)\1", "$2");
        s = Regex.Replace(s, @"(?<!\w)([*_])(.+?)\1(?!\w)", "$2");

        // 行内代码 `code`
        s = Regex.Replace(s, @"`([^`]+)`", "$1");

        // 多余空行折叠
        s = Regex.Replace(s, @"\n{3,}", "\n\n");

        return s.Trim();
    }
}
