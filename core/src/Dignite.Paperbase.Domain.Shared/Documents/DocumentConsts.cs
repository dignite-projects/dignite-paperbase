namespace Dignite.Paperbase.Documents;

public static class DocumentConsts
{
    public static int MaxOriginalFileBlobNameLength { get; set; } = 512;

    public static int MaxDocumentTypeCodeLength { get; set; } = 128;

    public static int MaxTitleLength { get; set; } = 256;

    /// <summary>OCR / 抽取阶段检测到的语言 tag（ISO 639-1 或 IETF）。</summary>
    public static int MaxLanguageLength { get; set; } = 16;
}
