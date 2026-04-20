using System.IO;

namespace Dignite.Paperbase.Domain.Documents;

/// <summary>批量上传时的单个文件项</summary>
public class UploadFileItem
{
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSize { get; set; }
    public Stream Stream { get; set; } = default!;
}
