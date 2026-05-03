using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.TextExtraction;

/// <summary>
/// 数字版文档（PDF/Word/HTML/纯文本/CSV/RTF/EPUB 等）→ Markdown Provider 抽象。
/// 与 <c>IOcrProvider</c> 对称：OCR 处理图像/扫描件，本接口处理具备数字文本层的文件。
/// 仅供 Dignite.Paperbase.TextExtraction 内部使用。
/// </summary>
public interface IMarkdownTextProvider
{
    bool CanHandle(string contentType, string fileExtension);

    Task<MarkdownExtractionResult> ExtractAsync(
        Stream fileStream,
        MarkdownExtractionContext context,
        CancellationToken cancellationToken = default);
}
