using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.TextExtraction;

[DependsOn(typeof(PaperbaseAbstractionsModule))]
public class PaperbaseTextExtractionModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<ITextExtractor, DefaultTextExtractor>();
    }
}
