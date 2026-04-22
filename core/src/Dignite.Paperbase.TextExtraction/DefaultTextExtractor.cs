using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Ocr;
using Dignite.Paperbase.Abstractions.TextExtraction;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dignite.Paperbase.TextExtraction;

/// <summary>
/// 默认文本提取器。
/// - Physical (image/*)：直接调用 IOcrProvider。
/// - Digital (application/pdf)：先尝试 PdfPig 提取原生文字层；
///   若提取为空则回退到 IOcrProvider（针对扫描版 PDF）。
/// - 其他 Digital 格式：暂不支持，返回空文本（Slice 3 补充）。
/// </summary>
public class DefaultTextExtractor : ITextExtractor
{
    private readonly IOcrProvider _ocrProvider;

    public DefaultTextExtractor(IOcrProvider ocrProvider)
    {
        _ocrProvider = ocrProvider;
    }

    public virtual async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var contentType = context.ContentType ?? string.Empty;

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractViaOcrAsync(fileStream, context, cancellationToken);
        }

        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPdfAsync(fileStream, context, cancellationToken);
        }

        // 其他格式（Slice 3 补充）
        return new TextExtractionResult
        {
            ExtractedText = string.Empty,
            Confidence = 0,
            PageCount = 0
        };
    }

    protected virtual async Task<TextExtractionResult> ExtractPdfAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken)
    {
        // 将流读入内存（PdfPig 需要可寻址流）
        byte[] pdfBytes;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms, cancellationToken);
            pdfBytes = ms.ToArray();
        }

        var digitalText = ExtractDigitalPdfText(pdfBytes);

        if (!string.IsNullOrWhiteSpace(digitalText))
        {
            return new TextExtractionResult
            {
                ExtractedText = digitalText,
                Confidence = 1.0,
                PageCount = CountPdfPages(pdfBytes),
                Metadata = { ["ExtractionMethod"] = "PdfPig" }
            };
        }

        // 文字层为空 → 扫描版 PDF，回退 OCR
        using var ocrStream = new MemoryStream(pdfBytes);
        return await ExtractViaOcrAsync(ocrStream, context, cancellationToken);
    }

    protected virtual string ExtractDigitalPdfText(byte[] pdfBytes)
    {
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var pageText = string.Join(" ", page.GetWords().Select((Word w) => w.Text));
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine(pageText);
                }
            }
            return sb.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    protected virtual int CountPdfPages(byte[] pdfBytes)
    {
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            return document.NumberOfPages;
        }
        catch
        {
            return 0;
        }
    }

    protected virtual async Task<TextExtractionResult> ExtractViaOcrAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken)
    {
        var options = new OcrOptions
        {
            LanguageHints = context.LanguageHints
        };

        var ocrResult = await _ocrProvider.RecognizeAsync(fileStream, options, cancellationToken);

        return new TextExtractionResult
        {
            ExtractedText = ocrResult.FullText,
            Confidence = ocrResult.Pages.SelectMany(p => p.Blocks).Any()
                ? ocrResult.Pages.SelectMany(p => p.Blocks).Average(b => b.Confidence)
                : 0,
            PageCount = ocrResult.Pages.Count,
            Metadata = ocrResult.Metadata
        };
    }
}
