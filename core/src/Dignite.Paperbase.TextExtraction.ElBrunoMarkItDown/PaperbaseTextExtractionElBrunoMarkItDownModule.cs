using ElBruno.MarkItDotNet;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown;

[DependsOn(typeof(PaperbaseTextExtractionModule))]
public class PaperbaseTextExtractionElBrunoMarkItDownModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 注册 ElBruno.MarkItDotNet 内部 ConverterRegistry / MarkdownService / 内置 12 个 Converter
        context.Services.AddMarkItDotNet();
    }
}
