using System.IO;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Ocr;

/// <summary>
/// OCR 服务 Provider 接口。实现此接口以对接具体的 OCR 服务（云端或本地）。
/// 仅供 Dignite.Paperbase.TextExtraction 内部使用，不暴露给核心模块。
/// </summary>
public interface IOcrProvider
{
    Task<OcrResult> RecognizeAsync(Stream fileStream, OcrOptions options);
}
