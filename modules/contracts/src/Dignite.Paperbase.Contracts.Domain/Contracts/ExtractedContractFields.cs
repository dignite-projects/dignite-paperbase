using System;
using System.Collections.Generic;
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

    public double ExtractionConfidence { get; set; }

    public bool NeedsReview { get; set; }

    public static ExtractedContractFields FromDictionary(IDictionary<string, string?> fields)
    {
        var result = new ExtractedContractFields
        {
            Title = Get(fields, "Title"),
            ContractNumber = Get(fields, "ContractNumber"),
            PartyAName = Get(fields, "PartyAName"),
            PartyBName = Get(fields, "PartyBName"),
            CounterpartyName = Get(fields, "CounterpartyName"),
            SignedDate = GetDate(fields, "SignedDate"),
            EffectiveDate = GetDate(fields, "EffectiveDate"),
            ExpirationDate = GetDate(fields, "ExpirationDate"),
            TotalAmount = GetDecimal(fields, "TotalAmount"),
            Currency = Get(fields, "Currency") ?? "JPY",
            AutoRenewal = GetBool(fields, "AutoRenewal"),
            TerminationNoticeDays = GetInt(fields, "TerminationNoticeDays"),
            GoverningLaw = Get(fields, "GoverningLaw"),
            Summary = Get(fields, "Summary")
        };

        var filledRequired = (result.Title != null ? 1 : 0)
            + (result.SignedDate.HasValue ? 1 : 0)
            + (result.ExpirationDate.HasValue ? 1 : 0);

        result.NeedsReview = filledRequired < 3;
        result.ExtractionConfidence = Math.Min(0.95, 0.70 + filledRequired * 0.08);

        return result;
    }

    private static string? Get(IDictionary<string, string?> f, string key)
        => f.TryGetValue(key, out var v) ? v : null;

    private static DateTime? GetDate(IDictionary<string, string?> f, string key)
    {
        if (!f.TryGetValue(key, out var v) || v is null) return null;
        return DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    private static decimal? GetDecimal(IDictionary<string, string?> f, string key)
    {
        if (!f.TryGetValue(key, out var v) || v is null) return null;
        return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool? GetBool(IDictionary<string, string?> f, string key)
    {
        if (!f.TryGetValue(key, out var v) || v is null) return null;
        return bool.TryParse(v, out var b) ? b : null;
    }

    private static int? GetInt(IDictionary<string, string?> f, string key)
    {
        if (!f.TryGetValue(key, out var v) || v is null) return null;
        return int.TryParse(v, out var i) ? i : null;
    }
}
