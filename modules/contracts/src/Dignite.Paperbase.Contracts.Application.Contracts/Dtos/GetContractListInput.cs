using System;
using Volo.Abp.Application.Dtos;

namespace Dignite.Paperbase.Contracts.Dtos;

public class GetContractListInput : PagedAndSortedResultRequestDto
{
    public Guid? DocumentId { get; set; }

    public string? CounterpartyKeyword { get; set; }

    public DateTime? ExpirationDateFrom { get; set; }

    public DateTime? ExpirationDateTo { get; set; }

    public bool? NeedsReview { get; set; }

    public decimal? TotalAmountMin { get; set; }

    public decimal? TotalAmountMax { get; set; }
}
