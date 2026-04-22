using System.ComponentModel.DataAnnotations;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Documents;

public class UploadDocumentInput
{
    /// <summary>上传文件流，由 ABP 以 multipart/form-data 绑定。</summary>
    [Required]
    public IRemoteStreamContent File { get; set; } = default!;

    /// <summary>原始文件名；未显式传入时使用 File.FileName。</summary>
    public string? FileName { get; set; }
}
