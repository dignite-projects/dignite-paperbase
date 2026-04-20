using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Services;

namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// 将多个文件流合并为单个 PDF，写入正式 blob 容器。
/// 仅在 BatchUploadAsync 收到 mergeIntoOne = true 时调用。
/// 实现在 Slice 1 补充。
/// </summary>
public class DocumentMerger : DomainService
{
    /// <summary>
    /// 按 files 顺序将图片或 PDF 页面合并为一个 PDF，
    /// 存入正式 blob 容器，返回 blobName。
    /// </summary>
    public virtual Task<string> MergeAsync(
        IList<UploadFileItem> files,
        CancellationToken cancellationToken = default)
    {
        throw new System.NotImplementedException("DocumentMerger is implemented in Slice 1.");
    }
}
