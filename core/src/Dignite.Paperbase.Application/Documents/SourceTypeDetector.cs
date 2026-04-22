using Dignite.Paperbase.Documents;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents;

/// <summary>
/// 根据 ContentType 判断文件来源类型。
/// PDF 的数字/物理路径判断由 DefaultTextExtractor 内部处理（尝试 PdfPig，无文字则回退 OCR）。
/// </summary>
public class SourceTypeDetector : ITransientDependency
{
    public virtual SourceType Detect(string contentType)
    {
        if (contentType.StartsWith("image/", System.StringComparison.OrdinalIgnoreCase))
        {
            return SourceType.Physical;
        }

        return SourceType.Digital;
    }
}
