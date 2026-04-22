using Dignite.Paperbase.Abstractions;
using Dignite.Paperbase.Abstractions.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Ocr.AzureDocumentIntelligence;

[DependsOn(typeof(PaperbaseAbstractionsModule))]
public class PaperbaseAzureDocumentIntelligenceModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<IOcrProvider, AzureDocumentIntelligenceOcrProvider>();

        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<AzureDocumentIntelligenceOptions>(
            configuration.GetSection("AzureDocumentIntelligence"));
    }
}
