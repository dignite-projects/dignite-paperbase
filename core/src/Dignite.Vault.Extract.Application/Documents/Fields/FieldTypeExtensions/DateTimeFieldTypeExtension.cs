using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Date;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// The kernel's <c>DateTime</c> field type. v2's <c>Date</c> and <c>DateTime</c> are one type here, told
/// apart by <see cref="DateTimeConfiguration.InputMode"/> (Date / DateTime / Month); all three normalize to
/// midnight on write so equality stays equality, and Month additionally pins the day to 1 - its day carries
/// no information, and the egress emits year and month only.
/// </summary>
public class DateTimeFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    /// <summary>
    /// The date-time shapes accepted <b>on the way in</b>. Output stays <see cref="FieldValueFormats.DateTime"/>
    /// alone - that one is a frozen wire contract and this does not widen it; every accepted shape parses to
    /// the same <see cref="DateTime"/> and is re-emitted canonically. The extra three exist because a browser
    /// owns the value before the server sees it, and a datetime-local input hands back a couple of near-miss
    /// shapes depending on whether seconds are zero.
    /// </summary>
    private static readonly string[] AcceptedDateTimeFormats =
    {
        FieldValueFormats.DateTime,
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm"
    };

    public override string FieldTypeName => DateTimeFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;
        var dateTimeConfig = new DateTimeConfiguration(configuration);

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (dateTimeConfig.InputMode == DateTimeInputMode.DateTime)
        {
            if (!DateTime.TryParseExact(
                    text, AcceptedDateTimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var moment)
                || IsOutOfRange(moment, dateTimeConfig))
            {
                return false;
            }

            result = moment;
            return true;
        }

        // Date mode stores midnight, which is what keeps an equality filter on a date an equality filter
        // now that Date and DateTime share one field type. Month mode stores the first of the month at
        // midnight for the same reason: the day carries no information, so pinning it to 1 keeps the value
        // an ordinary DateTime that sorts, ranges and indexes like every other, and the egress reads only
        // the year and month back out.
        var isMonth = dateTimeConfig.InputMode == DateTimeInputMode.Month;

        if (!DateOnly.TryParseExact(
                text, DateTimeInputModeFormats.Format(dateTimeConfig.InputMode),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        if (isMonth)
        {
            date = new DateOnly(date.Year, date.Month, 1);
        }

        var midnight = date.ToDateTime(TimeOnly.MinValue);
        if (IsOutOfRange(midnight, dateTimeConfig))
        {
            return false;
        }

        result = midnight;
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration)
    {
        var dateTimeConfig = new DateTimeConfiguration(configuration);

        // Date, DateTime and Month are one field type in v3, told apart by InputMode - so the pattern the
        // model is held to comes from configuration rather than from the type. Asking a date-only field
        // for hours and minutes would invent precision the document does not have, and asking a month
        // field for a day would invent one outright.
        return dateTimeConfig.InputMode switch
        {
            DateTimeInputMode.DateTime => new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$",
                ["description"] = "An offset-free ISO-8601 local date-time string in YYYY-MM-DDThh:mm:ss format, or null when absent."
            },
            DateTimeInputMode.Month => new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}$",
                ["description"] = "An ISO-8601 year and month in YYYY-MM format, with no day, or null when absent."
            },
            _ => new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}-\d{2}$",
                ["description"] = "An ISO-8601 date string in YYYY-MM-DD format, or null when absent."
            }
        };
    }

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
    {
        if (!TryReadStoredDateTime(value, out var moment))
        {
            return null;
        }

        // The format follows InputMode, which is what preserves v2's split: a Date field emitted a bare
        // date and a DateTime field emitted the offset-free local shape. A Month field emits year and
        // month only - its stored day is pinned to 1 and carries nothing.
        var format = DateTimeInputModeFormats.Format(new DateTimeConfiguration(configuration).InputMode);
        return JsonSerializer.SerializeToElement(moment.ToString(format, CultureInfo.InvariantCulture));
    }

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
    {
        if (!TryReadStoredDateTime(value, out var moment))
        {
            return null;
        }

        var format = DateTimeInputModeFormats.Format(new DateTimeConfiguration(configuration).InputMode);
        return moment.ToString(format, CultureInfo.InvariantCulture);
    }

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value)
    {
        // One fixed format regardless of InputMode: the fingerprint only needs internal consistency (the
        // same stored value always hashes the same way), not to match the mode-dependent egress shape, and
        // all three modes already store as an ordinary midnight-or-pinned DateTime.
        if (!TryReadStoredDateTime(value, out var moment))
        {
            return Array.Empty<string>();
        }

        return new[] { moment.ToString(FieldValueFormats.DateTime, CultureInfo.InvariantCulture) };
    }

    /// <summary>
    /// Reads back an already-stored value (a fresh in-memory <see cref="DateTime"/>, or a reloaded
    /// <see cref="JsonElement"/>) - unlike <see cref="TryRead"/>, this never needs the browser-input-format
    /// tolerance, because it only ever sees a value this same type already accepted on the way in.
    /// </summary>
    private static bool TryReadStoredDateTime(object value, out DateTime result) => value switch
    {
        DateTime dt => Assign(dt, out result),
        JsonElement { ValueKind: JsonValueKind.String } e => e.TryGetDateTime(out result),
        _ => DateTime.TryParse(
            FieldTypeExtensionHelpers.ReadAsString(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
    };

    private static bool Assign(DateTime value, out DateTime result)
    {
        result = value;
        return true;
    }

    private static bool IsOutOfRange(DateTime value, DateTimeConfiguration configuration)
        => (configuration.Max.HasValue && value > configuration.Max.Value)
           || (configuration.Min.HasValue && value < configuration.Min.Value);
}
