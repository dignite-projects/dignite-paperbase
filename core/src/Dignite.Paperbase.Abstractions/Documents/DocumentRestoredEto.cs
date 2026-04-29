using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Abstractions.Documents;

[EventName("Paperbase.Document.Restored")]
public class DocumentRestoredEto
{
    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string? DocumentTypeCode { get; set; }
}
