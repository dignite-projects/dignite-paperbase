using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 文档分类能力端口。纯能力——收文本，返回 TypeCode + 置信度。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IDocumentClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken = default);
}
