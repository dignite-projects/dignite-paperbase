using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文档被彻底删除（物理删除）时发布。
/// 与 <see cref="DocumentDeletedEto"/>（软删除信号）的区别：
/// 软删除可被恢复，业务模块应将自身数据置为可恢复的归档状态；
/// 彻底删除不可恢复，业务模块应物理删除从该文档派生的数据（合同、抽取字段等）。
/// </summary>
[EventName("Paperbase.Document.PermanentlyDeleted")]
public class DocumentPermanentlyDeletedEto
{
    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? DocumentTypeCode { get; set; }
}
