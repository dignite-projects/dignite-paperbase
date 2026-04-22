using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.Ocr;

/// <summary>
/// OCR 识别能力端口。纯能力——收文件流，返回识别结果；不知道 Document 聚合。
/// 实现：Dignite.Paperbase.Ocr.AzureDocumentIntelligence
/// </summary>
public interface IOcrProvider
{
    Task<OcrResult> RecognizeAsync(
        Stream fileStream,
        OcrOptions options,
        CancellationToken cancellationToken = default);
}
