using System;
using Dignite.Paperbase.Ocr;

namespace Dignite.Paperbase.Ocr.AzureDocumentIntelligence;

public static class AzureDocumentIntelligenceExtensions
{
    /// <summary>
    /// 注册 Azure Document Intelligence 作为活跃的 OCR Provider。
    /// 具体凭证通过 Configure&lt;AzureDocumentIntelligenceOptions&gt; 或 appsettings.json 配置。
    /// </summary>
    public static PaperbaseOcrOptions UseAzureDocumentIntelligence(
        this PaperbaseOcrOptions options,
        Action<AzureDocumentIntelligenceOptions>? configure = null)
    {
        options.UseProvider<AzureDocumentIntelligenceOcrProvider>();
        return options;
    }
}
