using System;
using Dignite.Paperbase.Documents;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class GetDocumentListInput : PagedAndSortedResultRequestDto
{
    public DocumentLifecycleStatus? LifecycleStatus { get; set; }
    public string? DocumentTypeCode { get; set; }

    /// <summary>
    /// true = Classification 最新 Run 的 ResultCode 为 LowConfidence 或 BudgetExceeded
    /// </summary>
    public bool? NeedsManualReview { get; set; }
}
