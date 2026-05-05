using System;
using System.Globalization;

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

    public bool? AutoRenewal { get; set; }

    public int? TerminationNoticeDays { get; set; }

    public string? GoverningLaw { get; set; }

    public string? Summary { get; set; }

    public double? ExtractionConfidence { get; set; }

    public bool NeedsReview { get; set; }

    public ContractReviewStatus ReviewStatus { get; set; }

    public static ExtractedContractFields FromAgentResult(ContractExtractionResult result)
    {
        var fields = new ExtractedContractFields
        {
            Title = result.Title,
            ContractNumber = result.ContractNumber,
            PartyAName = result.PartyAName,
            PartyBName = result.PartyBName,
            CounterpartyName = result.CounterpartyName,
            SignedDate = ParseDate(result.SignedDate),
            EffectiveDate = ParseDate(result.EffectiveDate),
            ExpirationDate = ParseDate(result.ExpirationDate),
            TotalAmount = result.TotalAmount,
            Currency = string.IsNullOrEmpty(result.Currency) ? "JPY" : result.Currency,
            AutoRenewal = result.AutoRenewal,
            TerminationNoticeDays = result.TerminationNoticeDays,
            GoverningLaw = result.GoverningLaw,
            Summary = result.Summary
        };

        fields.ExtractionConfidence = NormalizeConfidence(result.ExtractionConfidence);
        fields.ReviewStatus = ContractReviewStatus.Pending;
        fields.NeedsReview = true;

        return fields;
    }

    private static double? NormalizeConfidence(double? value)
    {
        if (!value.HasValue || value.Value < 0 || value.Value > 1)
        {
            return null;
        }

        return value.Value;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
