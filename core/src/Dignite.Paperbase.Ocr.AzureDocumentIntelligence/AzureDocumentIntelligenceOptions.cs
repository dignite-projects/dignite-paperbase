namespace Dignite.Paperbase.Ocr.AzureDocumentIntelligence;

public class AzureDocumentIntelligenceOptions
{
    public string Endpoint { get; set; } = default!;
    public string ApiKey { get; set; } = default!;

    /// <summary>使用的预建模型。默认 "prebuilt-read"，日文识别推荐 "prebuilt-document"。</summary>
    public string ModelId { get; set; } = "prebuilt-read";
}
