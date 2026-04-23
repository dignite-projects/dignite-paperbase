using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Ocr;
using Dignite.Paperbase.TextExtraction.Digital;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.TextExtraction;

[DependsOn(typeof(PaperbaseAbstractionsModule), typeof(PaperbaseOcrModule))]
public class PaperbaseTextExtractionModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // internal 类无法通过 ITransientDependency 自动注册，需手动注册
        context.Services.AddTransient<IDigitalTextExtractor, PdfTextExtractor>();
        context.Services.AddTransient<IDigitalTextExtractor, WordTextExtractor>();
        context.Services.AddTransient<IDigitalTextExtractor, PlainTextExtractor>();
        context.Services.AddTransient<IDigitalTextExtractorFactory, DigitalTextExtractorFactory>();
    }
}
