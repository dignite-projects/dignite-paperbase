using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ocr;
using Dignite.Paperbase.TextExtraction.Digital;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction;

public class DefaultTextExtractor : ITextExtractor, ITransientDependency
{
    private readonly IOcrProvider _ocrProvider;
    private readonly IDigitalTextExtractorFactory _digitalExtractorFactory;
    private readonly PaperbaseOcrOptions _ocrOptions;

    public ILogger<DefaultTextExtractor> Logger { get; set; } = NullLogger<DefaultTextExtractor>.Instance;

    public DefaultTextExtractor(
        IOcrProvider ocrProvider,
        IDigitalTextExtractorFactory digitalExtractorFactory,
        IOptions<PaperbaseOcrOptions> ocrOptions)
    {
        _ocrProvider = ocrProvider;
        _digitalExtractorFactory = digitalExtractorFactory;
        _ocrOptions = ocrOptions.Value;
    }

    public virtual async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var path = DispatchPath(context);
        return path switch
        {
            ExtractionPath.Digital => await ExtractDigitalAsync(fileStream, context),
            ExtractionPath.Physical => await ExtractByOcrAsync(fileStream, context),
            ExtractionPath.Pdf => await ExtractPdfAsync(fileStream, context),
            _ => await ExtractByOcrAsync(fileStream, context)
        };
    }

    private ExtractionPath DispatchPath(TextExtractionContext ctx)
    {
        var ext = (ctx.FileExtension ?? string.Empty).ToLowerInvariant();
        var ct = (ctx.ContentType ?? string.Empty).ToLowerInvariant();

        if (ext is ".docx" or ".doc" or ".md" or ".txt" or ".csv" or ".rtf")
            return ExtractionPath.Digital;
        if (ct.StartsWith("text/"))
            return ExtractionPath.Digital;
        if (ext is ".jpg" or ".jpeg" or ".png" or ".tiff" or ".tif" or ".bmp" or ".webp")
            return ExtractionPath.Physical;
        if (ext == ".pdf")
            return ExtractionPath.Pdf;

        return ExtractionPath.Physical;
    }

    protected virtual async Task<TextExtractionResult> ExtractPdfAsync(
        Stream fileStream,
        TextExtractionContext ctx)
    {
        byte[] pdfBytes;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms);
            pdfBytes = ms.ToArray();
        }

        try
        {
            using var pdfStream = new MemoryStream(pdfBytes);
            return await ExtractDigitalAsync(pdfStream, ctx);
        }
        catch (NoTextLayerException)
        {
            using var ocrStream = new MemoryStream(pdfBytes);
            return await ExtractByOcrAsync(ocrStream, ctx);
        }
    }

    protected virtual async Task<TextExtractionResult> ExtractDigitalAsync(
        Stream fileStream,
        TextExtractionContext ctx)
    {
        var extractor = _digitalExtractorFactory.GetExtractor(ctx.ContentType ?? string.Empty, ctx.FileExtension ?? string.Empty);
        var text = await extractor.ExtractAsync(fileStream, ctx.ContentType ?? string.Empty);

        Logger.LogDebug("Digital extraction completed using {Extractor}", extractor.GetType().Name);

        return new TextExtractionResult
        {
            ExtractedText = text,
            Confidence = 1.0,
            DetectedLanguage = null,
            PageCount = 0,
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

        return new TextExtractionResult
        {
            ExtractedText = result.RawText,
            Confidence = result.Confidence,
            DetectedLanguage = result.DetectedLanguage,
            PageCount = result.PageCount,
            UsedOcr = true,
        };
    }
}

internal enum ExtractionPath { Digital, Physical, Pdf }
