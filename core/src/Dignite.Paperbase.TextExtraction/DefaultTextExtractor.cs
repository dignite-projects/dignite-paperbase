using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction;

public class DefaultTextExtractor : ITextExtractor, ITransientDependency
{
    private readonly IOcrProvider _ocrProvider;
    private readonly IMarkdownTextProvider _markdownProvider;
    private readonly PaperbaseOcrOptions _ocrOptions;

    public ILogger<DefaultTextExtractor> Logger { get; set; } = NullLogger<DefaultTextExtractor>.Instance;

    public DefaultTextExtractor(
        IOcrProvider ocrProvider,
        IMarkdownTextProvider markdownProvider,
        IOptions<PaperbaseOcrOptions> ocrOptions)
    {
        _ocrProvider = ocrProvider;
        _markdownProvider = markdownProvider;
        _ocrOptions = ocrOptions.Value;
    }

    public virtual async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsImageFormat(context.FileExtension))
        {
            return await ExtractByOcrAsync(fileStream, context);
        }

        // 用单一 MemoryStream 横跨 Markdown Provider + 可能的 OCR 回退两次读取：
        // 输入流来自 blob 存储可能不可 seek，且 ElBruno 内部 PdfPig/OpenXml 等
        // 解析器要求 seekable stream，故必须缓冲。
        // 已知限制：超大文件（GB 级扫描 PDF）会全量驻留内存，需要时改为临时文件路径。
        using var seekable = new MemoryStream();
        await fileStream.CopyToAsync(seekable, cancellationToken);
        seekable.Position = 0;

        var md = await _markdownProvider.ExtractAsync(
            seekable,
            new MarkdownExtractionContext
            {
                ContentType = context.ContentType ?? string.Empty,
                FileExtension = context.FileExtension ?? string.Empty,
                LanguageHints = context.LanguageHints
            },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(md.Markdown) && IsPdfExtension(context.FileExtension))
        {
            Logger.LogDebug("Markdown provider produced no content for PDF; falling back to OCR.");
            seekable.Position = 0;
            return await ExtractByOcrAsync(seekable, context);
        }

        Logger.LogDebug("Markdown extraction completed using {Provider}", _markdownProvider.GetType().Name);

        return new TextExtractionResult
        {
            Markdown = md.Markdown,
            Confidence = 1.0,
            DetectedLanguage = md.DetectedLanguage,
            PageCount = md.PageCount,
            UsedOcr = false,
        };
    }

    protected virtual async Task<TextExtractionResult> ExtractByOcrAsync(
        Stream fileStream,
        TextExtractionContext ctx)
    {
        var options = new OcrOptions
        {
            ContentType = ctx.ContentType ?? string.Empty,
            LanguageHints = ctx.LanguageHints?.Count > 0
                ? ctx.LanguageHints
                : (IList<string>)_ocrOptions.DefaultLanguageHints,
            IncludeBlockPositions = false
        };

        var result = await _ocrProvider.RecognizeAsync(fileStream, options);

        Logger.LogDebug("OCR extraction completed using {Provider}", _ocrProvider.GetType().Name);

        // Provider 没有原生 Markdown 输出（如 PaddleOcr PP-OCRv4 模式）时，
        // 用 RawText 作为退化 Markdown：无标题/表格/列表的纯段落是合法 Markdown，
        // 下游 Markdown-aware 切块器按段落工作不会失败。
        var markdown = !string.IsNullOrEmpty(result.Markdown)
            ? result.Markdown
            : (result.RawText ?? string.Empty);

        return new TextExtractionResult
        {
            Markdown = markdown,
            Confidence = result.Confidence,
            DetectedLanguage = result.DetectedLanguage,
            PageCount = result.PageCount,
            UsedOcr = true,
        };
    }

    protected virtual bool IsImageFormat(string? fileExtension)
    {
        if (string.IsNullOrWhiteSpace(fileExtension)) return false;
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".tiff" or ".tif" or ".bmp" or ".webp" or ".gif";
    }

    protected virtual bool IsPdfExtension(string? fileExtension)
        => string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);
}
