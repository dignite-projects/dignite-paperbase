using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class GetDocumentListInput : PagedAndSortedResultRequestDto
{
    public DocumentLifecycleStatus? LifecycleStatus { get; set; }
    public string? DocumentTypeCode { get; set; }

    public DocumentReviewStatus? ReviewStatus { get; set; }
}
