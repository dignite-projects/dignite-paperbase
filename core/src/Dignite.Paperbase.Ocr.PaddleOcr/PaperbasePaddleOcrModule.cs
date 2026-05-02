using System;
using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        context.Services
            .AddOptions<PaddleOcrOptions>()
            .PostConfigure<IOptions<PaperbaseOcrOptions>>((paddleOcrOpts, ocrOpts) =>
            {
                ocrOpts.Value
                    .GetProviderConfigure<Action<PaddleOcrOptions>>()
                    ?.Invoke(paddleOcrOpts);
            });

        context.Services.AddHttpClient();
    }
}
