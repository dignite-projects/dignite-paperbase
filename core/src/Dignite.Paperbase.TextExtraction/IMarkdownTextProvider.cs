using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.TextExtraction;

/// <summary>
/// 数字版文档（PDF/Word/HTML/纯文本/CSV/RTF/EPUB 等）→ Markdown Provider 抽象。
/// 处理具备数字文本层的文件，与处理图像/扫描件的 <c>IOcrProvider</c> 互补。
/// 消费者固定为 <c>DefaultTextExtractor</c>，实现方由独立 Provider 模块
/// （如 <c>Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown</c>）提供，
/// Host 侧通过 <c>DependsOn</c> 选择启用一个实现。
/// </summary>
/// <remarks>
/// <b>Markdown-first 契约</b>：实现方<b>必须</b>把抽取结果填充到
/// <see cref="MarkdownExtractionResult"/> 的 Markdown 字段，
/// 而<b>不能</b>退回 plain text。Markdown 标题、表格、列表是后续向量化切块（结构感知）
/// 与 LLM 理解的关键语义信号；任何"plain text fallback"都属于设计违规。
/// </remarks>
public interface IMarkdownTextProvider
{
    bool CanHandle(string contentType, string fileExtension);

    Task<MarkdownExtractionResult> ExtractAsync(
        Stream fileStream,
        MarkdownExtractionContext context,
        CancellationToken cancellationToken = default);
}
