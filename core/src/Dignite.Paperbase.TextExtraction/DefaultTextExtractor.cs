using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ocr;
using Dignite.Paperbase.TextExtraction.OcrProfiles;
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
    private readonly IOcrQualityScorer _qualityScorer;

    public ILogger<DefaultTextExtractor> Logger { get; set; } = NullLogger<DefaultTextExtractor>.Instance;

    public DefaultTextExtractor(
        IOcrProvider ocrProvider,
        IMarkdownTextProvider markdownProvider,
        IOptions<PaperbaseOcrOptions> ocrOptions,
        IOcrQualityScorer qualityScorer)
    {
        _ocrProvider = ocrProvider;
        _markdownProvider = markdownProvider;
        _ocrOptions = ocrOptions.Value;
        _qualityScorer = qualityScorer;
    }

    public virtual async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsImageFormat(context.FileExtension))
        {
            return await ExtractByOcrAsync(fileStream, context, cancellationToken);
        }

        // 用单一 MemoryStream 横跨 Markdown Provider + 可能的 OCR 回退两次读取：
        // 输入流来自 blob 存储可能不可 seek，且 ElBruno 内部 PdfPig/OpenXml 等
        // 解析器要求 seekable stream，故必须缓冲。
        // 已知限制：超大文件（GB 级扫描 PDF）会全量驻留内存，需要时改为临时文件路径。
        using var seekable = new MemoryStream();
        await fileStream.CopyToAsync(seekable, cancellationToken);
        seekable.Position = 0;

        var md = await _markdownProvider.ExtractAsync(seekable, context, cancellationToken);

        if (!HasMeaningfulText(md.Markdown) && IsPdfExtension(context.FileExtension))
        {
            Logger.LogDebug("Markdown provider produced no meaningful text for PDF; falling back to OCR.");
            seekable.Position = 0;
            return await ExtractByOcrAsync(seekable, context, cancellationToken);
        }

        Logger.LogDebug("Markdown extraction completed using {Provider}", _markdownProvider.GetType().Name);
        return md;
    }

    protected virtual async Task<TextExtractionResult> ExtractByOcrAsync(
        Stream fileStream,
        TextExtractionContext ctx,
        CancellationToken cancellationToken)
    {
        using var seekable = new MemoryStream();
        await fileStream.CopyToAsync(seekable, cancellationToken);

        var languageHints = ctx.LanguageHints?.Count > 0
            ? ctx.LanguageHints
            : (IList<string>)_ocrOptions.DefaultLanguageHints;
        var requestedProfile = OcrProfileCodes.Normalize(ctx.OcrProfileCode ?? _ocrOptions.DefaultOcrProfileCode);

        // auto 不是真实 provider profile，只是编排入口：先信任 provider，以 general 跑一次全量 OCR，
        // 再由首次结果的质量信号经 scorer 决定是否值得换更专精的 profile 重试一次。
        // 不做事前 probe——结构信号事后从全量 Markdown 派生即可，无需为单页文档付双倍 OCR 成本。
        var initialProfile = requestedProfile == OcrProfileCodes.Auto
            ? OcrProfileCodes.General
            : requestedProfile;

        var initialResult = await RecognizeAsync(seekable, ctx, languageHints, initialProfile);
        var initialAssessment = _qualityScorer.Score(initialResult, initialProfile);

        var finalResult = initialResult;
        var finalProfile = initialProfile;
        var retrySelected = false;

        // 质量评分低且诊断明确时，用针对性 profile 重试一次，保留更好的全量结果。
        if (initialAssessment.IsLowQuality &&
            !string.IsNullOrWhiteSpace(initialAssessment.TargetedRetryProfileCode))
        {
            var retryProfile = initialAssessment.TargetedRetryProfileCode!;
            var retryResult = await RecognizeAsync(seekable, ctx, languageHints, retryProfile);
            var retryAssessment = _qualityScorer.Score(retryResult, retryProfile);

            if (_qualityScorer.IsBetter(retryAssessment, initialAssessment))
            {
                finalResult = retryResult;
                finalProfile = retryProfile;
                retrySelected = true;
            }
        }

        Logger.LogDebug(
            "OCR completed using {Provider}: requested {RequestedProfile}, effective {EffectiveProfile}, retried {Retried}.",
            finalResult.ProviderName ?? _ocrProvider.GetType().Name,
            requestedProfile,
            finalProfile,
            retrySelected);

        return new TextExtractionResult
        {
            Markdown = finalResult.Markdown,
            Confidence = finalResult.Confidence,
            DetectedLanguage = finalResult.DetectedLanguage,
            PageCount = finalResult.PageCount,
            UsedOcr = true
        };
    }

    private async Task<OcrResult> RecognizeAsync(
        Stream seekable,
        TextExtractionContext ctx,
        IList<string> languageHints,
        string profileCode)
    {
        seekable.Position = 0;
        return await _ocrProvider.RecognizeAsync(
            seekable,
            new OcrOptions
            {
                ContentType = ctx.ContentType ?? string.Empty,
                LanguageHints = languageHints,
                OcrProfileCode = profileCode
            });
    }

    protected virtual bool IsImageFormat(string? fileExtension)
    {
        if (string.IsNullOrWhiteSpace(fileExtension)) return false;
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".tiff" or ".tif" or ".bmp" or ".webp" or ".gif";
    }

    protected virtual bool HasMeaningfulText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return false;
        return markdown.Any(c => char.IsLetter(c) || char.IsDigit(c));
    }

    protected virtual bool IsPdfExtension(string? fileExtension)
        => string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);
}
