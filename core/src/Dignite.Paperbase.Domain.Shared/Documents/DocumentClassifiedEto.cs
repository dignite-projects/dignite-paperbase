using System;
using Volo.Abp.EventBus;

namespace Dignite.Paperbase.Documents;

[EventName("Dignite.Paperbase.DocumentClassified")]
public class DocumentClassifiedEto
{
    public string Version { get; set; } = "1.0";

    public Guid DocumentId { get; set; }

    public Guid? TenantId { get; set; }

    public string DocumentTypeCode { get; set; } = default!;

    public double ConfidenceScore { get; set; }
}
