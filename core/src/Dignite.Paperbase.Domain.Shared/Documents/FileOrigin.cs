using System;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 文件来源信息。写入后不可修改，是系统信任的锚点。
/// Physical 和 Digital 共用此值对象，部分字段仅适用于特定来源类型。
/// </summary>
public class FileOrigin
{
    /// <summary>上传时间</summary>
    public DateTime UploadedAt { get; private set; }

    /// <summary>上传操作人 ID</summary>
    public Guid UploadedByUserId { get; private set; }

    /// <summary>上传操作人名称（冗余存储，防止用户删除后丢失信息）</summary>
    public string UploadedByUserName { get; private set; } = default!;

    /// <summary>原始文件名（Digital 来源）</summary>
    public string? OriginalFileName { get; private set; }

    /// <summary>文件 MIME 类型</summary>
    public string ContentType { get; private set; } = default!;

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; private set; }

    /// <summary>扫描设备信息（Physical 来源，可选）</summary>
    public string? DeviceInfo { get; private set; }

    /// <summary>实际扫描时间（Physical 来源，可选，可能早于上传时间）</summary>
    public DateTime? ScannedAt { get; private set; }

    protected FileOrigin() { }

    public FileOrigin(
        DateTime uploadedAt,
        Guid uploadedByUserId,
        string uploadedByUserName,
        string contentType,
        long fileSize,
        string? originalFileName = null,
        string? deviceInfo = null,
        DateTime? scannedAt = null)
    {
        UploadedAt = uploadedAt;
        UploadedByUserId = uploadedByUserId;
        UploadedByUserName = uploadedByUserName;
        ContentType = contentType;
        FileSize = fileSize;
        OriginalFileName = originalFileName;
        DeviceInfo = deviceInfo;
        ScannedAt = scannedAt;
    }
}
