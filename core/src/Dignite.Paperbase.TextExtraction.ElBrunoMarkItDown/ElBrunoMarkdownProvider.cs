using System.IO;
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
        // 这里乐观返回 true，由 ExtractAsync 把不支持的格式转换为空 Markdown（交由 DefaultTextExtractor 处理）。
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
            return new MarkdownExtractionResult { Markdown = string.Empty };
        }

        return new MarkdownExtractionResult
        {
            Markdown = conversion.Markdown ?? string.Empty,
            PageCount = conversion.Metadata?.PageCount ?? 0,
            DetectedLanguage = null,
        };
    }
}
