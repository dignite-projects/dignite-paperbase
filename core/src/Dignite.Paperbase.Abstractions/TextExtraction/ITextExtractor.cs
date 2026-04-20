using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.TextExtraction;

/// <summary>
/// 文本提取能力端口。纯能力——收文件流与上下文，返回提取结果；
/// 不知道 Document 聚合、不访问仓储。
/// 实现：Dignite.Paperbase.TextExtraction
/// </summary>
public interface ITextExtractor
{
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default);
}
