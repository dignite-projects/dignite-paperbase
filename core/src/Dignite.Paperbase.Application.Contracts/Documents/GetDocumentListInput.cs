using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Documents;

public class GetDocumentListInput : PagedAndSortedResultRequestDto
{
    public string? LifecycleStatus { get; set; }
    public string? DocumentTypeCode { get; set; }
}
