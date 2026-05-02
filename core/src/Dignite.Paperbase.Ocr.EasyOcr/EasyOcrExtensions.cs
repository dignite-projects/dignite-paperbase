using System;
using Dignite.Paperbase.Ocr;

namespace Dignite.Paperbase.Ocr.EasyOcr;

public static class EasyOcrExtensions
{
    /// <summary>
    /// 注册 EasyOCR 作为活跃的 OCR Provider。
    /// 具体端点通过 Configure&lt;EasyOcrOptions&gt; 或 appsettings.json 配置。
    /// </summary>
    public static PaperbaseOcrOptions UseEasyOcr(
        this PaperbaseOcrOptions options,
        Action<EasyOcrOptions>? configure = null)
    {
        options.UseProvider<EasyOcrProvider>();
        options.SetProviderConfigure(configure);
        return options;
    }
}
