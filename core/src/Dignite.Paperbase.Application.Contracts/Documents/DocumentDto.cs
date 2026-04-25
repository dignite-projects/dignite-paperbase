using System;
using System.Collections.Generic;
using Dignite.Paperbase.Documents;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class DocumentDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }
    public string OriginalFileBlobName { get; set; } = default!;
    public SourceType SourceType { get; set; }
    public FileOriginDto FileOrigin { get; set; } = default!;
    public string? DocumentTypeCode { get; set; }
    public DocumentLifecycleStatus LifecycleStatus { get; set; }
    public DocumentReviewStatus ReviewStatus { get; set; }
    public double ClassificationConfidence { get; set; }
    public string? ClassificationReason { get; set; }
    public bool HasEmbedding { get; set; }
    public string? ExtractedText { get; set; }
    public DateTime CreationTime { get; set; }
    public IList<DocumentPipelineRunDto> PipelineRuns { get; set; } = new List<DocumentPipelineRunDto>();
}
