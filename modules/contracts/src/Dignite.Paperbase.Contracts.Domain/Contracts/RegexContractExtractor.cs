using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dignite.Paperbase.Contracts.Contracts;

public static class RegexContractExtractor
{
    private static readonly Regex DateRegex = new(
        @"(\d{4})年(\d{1,2})月(\d{1,2})日|(\d{4}-\d{2}-\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex AmountRegex = new(
        @"(契約金額|委託料|報酬|合計)\s*[：:。\s]*[¥￥]?\s*([\d,，]+)",
        RegexOptions.Compiled);

    private static readonly Regex ContractPeriodRegex = new(
        @"(\d{4}年\d{1,2}月\d{1,2}日).{0,12}(から|より|〜|~).{0,12}(\d{4}年\d{1,2}月\d{1,2}日)",
        RegexOptions.Compiled);

    private static readonly Regex ContractNumberRegex = new(
        @"(契約番号|管理番号|No\.?)\s*[：:\s]*([A-Za-z0-9\-_/]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PartyRegex = new(
        @"(甲|乙)\s*[：:]\s*(.+)",
        RegexOptions.Compiled);

    public static ExtractedContractFields Extract(string text)
    {
        var fields = new ExtractedContractFields
        {
            Title = ExtractTitle(text),
            ContractNumber = ExtractContractNumber(text),
            Currency = "JPY"
        };

        ExtractParties(text, fields);
        ExtractDates(text, fields);
        ExtractAmount(text, fields);

        var filledCount = new object?[]
        {
            fields.Title,
            fields.ContractNumber,
            fields.CounterpartyName,
            fields.SignedDate,
            fields.EffectiveDate,
            fields.ExpirationDate,
            fields.TotalAmount
        }.Count(x => x != null);

        fields.ExtractionConfidence = Math.Min(0.95, 0.25 + filledCount * 0.1);
        fields.NeedsReview = filledCount < 3;

        return fields;
    }

    private static string? ExtractTitle(string text)
    {
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Contains("契約書") || x.Contains("合意書"));
    }

    private static string? ExtractContractNumber(string text)
    {
        var match = ContractNumberRegex.Match(text);
        return match.Success ? match.Groups[2].Value.Trim() : null;
    }

    private static void ExtractParties(string text, ExtractedContractFields fields)
    {
        foreach (Match match in PartyRegex.Matches(text))
        {
            var marker = match.Groups[1].Value;
            var partyName = match.Groups[2].Value.Trim();

            if (marker == "甲")
            {
                fields.PartyAName = partyName;
            }
            else
            {
                fields.PartyBName = partyName;
            }
        }

        fields.CounterpartyName = fields.PartyBName ?? fields.PartyAName;
    }

    private static void ExtractDates(string text, ExtractedContractFields fields)
    {
        var periodMatch = ContractPeriodRegex.Match(text);
        if (periodMatch.Success)
        {
            fields.EffectiveDate = ParseDate(periodMatch.Groups[1].Value);
            fields.ExpirationDate = ParseDate(periodMatch.Groups[3].Value);
        }

        var firstDate = DateRegex.Matches(text)
            .Select(x => ParseDate(x.Value))
            .FirstOrDefault(x => x.HasValue);

        fields.SignedDate ??= firstDate;
    }

    private static void ExtractAmount(string text, ExtractedContractFields fields)
    {
        var match = AmountRegex.Match(text);
        if (!match.Success)
        {
            return;
        }

        var amountText = match.Groups[2].Value.Replace(",", string.Empty).Replace("，", string.Empty);
        if (decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            fields.TotalAmount = amount;
        }
    }

    private static DateTime? ParseDate(string value)
    {
        var match = DateRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups[4].Success &&
            DateTime.TryParseExact(match.Groups[4].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return isoDate;
        }

        if (int.TryParse(match.Groups[1].Value, out var year) &&
            int.TryParse(match.Groups[2].Value, out var month) &&
            int.TryParse(match.Groups[3].Value, out var day))
        {
            return new DateTime(year, month, day);
        }

        return null;
    }
}
