using System;

namespace Dignite.Paperbase.Contracts.Contracts;

public class ExtractedContractFields
{
    public string? Title { get; set; }

    public string? ContractNumber { get; set; }

    public string? PartyAName { get; set; }

    public string? PartyBName { get; set; }

    public string? CounterpartyName { get; set; }

    public DateTime? SignedDate { get; set; }

    public DateTime? EffectiveDate { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Currency { get; set; } = "JPY";

    public double ExtractionConfidence { get; set; }

    public bool NeedsReview { get; set; }
}
