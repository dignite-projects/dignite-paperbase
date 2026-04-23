using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Ocr.AzureDocumentIntelligence;

[DependsOn(typeof(PaperbaseOcrModule))]
public class PaperbaseAzureDocumentIntelligenceModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<AzureDocumentIntelligenceOptions>(
            configuration.GetSection("AzureDocumentIntelligence"));
    }
}
