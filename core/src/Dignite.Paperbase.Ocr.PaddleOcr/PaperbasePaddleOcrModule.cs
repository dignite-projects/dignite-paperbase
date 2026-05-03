using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Ocr.PaddleOcr;

[DependsOn(typeof(PaperbaseOcrModule))]
public class PaperbasePaddleOcrModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<PaddleOcrOptions>(
            configuration.GetSection("PaddleOcr"));

        context.Services.AddHttpClient();
    }
}
