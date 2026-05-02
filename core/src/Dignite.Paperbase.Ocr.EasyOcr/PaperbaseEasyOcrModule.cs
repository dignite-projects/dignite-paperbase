using System;
using Dignite.Paperbase.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Ocr.EasyOcr;

[DependsOn(typeof(PaperbaseOcrModule))]
public class PaperbaseEasyOcrModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<EasyOcrOptions>(
            configuration.GetSection("EasyOcr"));

        context.Services
            .AddOptions<EasyOcrOptions>()
            .PostConfigure<IOptions<PaperbaseOcrOptions>>((easyOcrOpts, ocrOpts) =>
            {
                ocrOpts.Value
                    .GetProviderConfigure<Action<EasyOcrOptions>>()
                    ?.Invoke(easyOcrOpts);
            });

        context.Services.AddHttpClient();
    }
}
