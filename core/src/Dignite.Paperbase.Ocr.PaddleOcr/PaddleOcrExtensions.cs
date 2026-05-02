using System;
using Dignite.Paperbase.Ocr;

namespace Dignite.Paperbase.Ocr.PaddleOcr;

public static class PaddleOcrExtensions
{
    /// <summary>
    /// 注册 PaddleOCR 作为活跃的 OCR Provider。
    /// 具体端点通过 Configure&lt;PaddleOcrOptions&gt; 或 appsettings.json 配置。
    /// </summary>
    public static PaperbaseOcrOptions UsePaddleOcr(
        this PaperbaseOcrOptions options,
        Action<PaddleOcrOptions>? configure = null)
    {
        options.UseProvider<PaddleOcrProvider>();
        options.SetProviderConfigure(configure);
        return options;
    }
}
