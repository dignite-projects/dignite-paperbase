using System;

namespace Dignite.Paperbase.Documents;

public class FileOriginDto
{
    public DateTime UploadedAt { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string UploadedByUserName { get; set; } = default!;
    public string? OriginalFileName { get; set; }
    public string ContentType { get; set; } = default!;
    public long FileSize { get; set; }
    public string? DeviceInfo { get; set; }
    public DateTime? ScannedAt { get; set; }
}
