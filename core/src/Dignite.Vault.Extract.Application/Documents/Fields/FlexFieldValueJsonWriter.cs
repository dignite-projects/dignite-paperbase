using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Select;
using Dignite.Vault.Extract.FlexFields;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Renders one v3 bag value as the canonical <see cref="JsonElement"/> the <c>ExtractedFields</c> egress
/// carries — the exact inverse of <see cref="FlexFieldValueReader"/>, and the v3 successor to
/// <c>FieldValueFormatter.ToJsonElement</c>.
/// <para>
/// This exists rather than serializing the bag value directly because <c>ExtractedFields</c> is a wire
/// contract that must not shift under the storage change (#560): a v2 <c>Date</c> field emitted
/// <c>"2026-03-14"</c>, and a bag holds it as a midnight <see cref="DateTime"/>, which
/// <c>JsonSerializer</c> would render as <c>"2026-03-14T00:00:00"</c>. Same value, different string, and
/// every downstream consumer parsing that field would see the shape change without a single error.
/// </para>
/// <para>
/// Note for the field-type dispatch consolidation tracked on #562: this is the fifth site keyed on
/// <c>FieldTypeName</c>, and it is the reader's mirror — the two belong to the same per-field-type
/// contract and should move together.
/// </para>
/// </summary>
public static class FlexFieldValueJsonWriter
{
    /// <summary>
    /// Renders <paramref name="value"/> for a field of type <paramref name="fieldTypeName"/>, or
    /// <c>null</c> when the value is absent.
    /// </summary>
    public static JsonElement? Write(
        object? value,
        string fieldTypeName,
        FieldConfigurationDictionary configuration)
    {
        if (value == null)
        {
            return null;
        }

        // Multi-valued types render as a JSON array of strings, the shape v2's AllowMultiple fields had.
        if (VaultExtractFieldTypes.IsMultiValue(fieldTypeName, configuration))
        {
            return JsonSerializer.SerializeToElement(ReadList(value));
        }

        if (string.Equals(fieldTypeName, DateTimeFieldType.ControlName, StringComparison.Ordinal))
        {
            if (!TryReadDateTime(value, out var moment))
            {
                return null;
            }

            // The format follows InputMode, which is what preserves v2's split: a Date field emitted a
            // bare date and a DateTime field emitted the offset-free local shape. A Month field emits
            // year and month only — its stored day is pinned to 1 and carries nothing. All three formats
            // are frozen in FieldValueFormats precisely because they are the egress contract.
            var format = DateTimeInputModeFormats.Format(
                new DateTimeConfiguration(configuration).InputMode);

            return JsonSerializer.SerializeToElement(moment.ToString(format, CultureInfo.InvariantCulture));
        }

        // Everything else round-trips as its own JSON type: a string stays a string, a decimal stays a
        // bare JSON number, a bool stays true/false. A JsonElement (a bag reloaded from the database) is
        // already in that form.
        return value is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(value);
    }

    private static List<string> ReadList(object value) => value switch
    {
        List<string> list => list,
        string single => new List<string> { single },
        JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString())
            .ToList(),
        JsonElement element => new List<string>
        {
            element.ValueKind == JsonValueKind.String ? element.GetString()! : element.ToString()
        },
        IEnumerable items => items.Cast<object?>()
            .Select(i => Convert.ToString(i, CultureInfo.InvariantCulture) ?? string.Empty).ToList(),
        _ => new List<string> { Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty }
    };

    private static bool TryReadDateTime(object value, out DateTime result) => value switch
    {
        DateTime dt => Assign(dt, out result),
        JsonElement { ValueKind: JsonValueKind.String } e => e.TryGetDateTime(out result),
        _ => DateTime.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
    };

    private static bool Assign(DateTime value, out DateTime result)
    {
        result = value;
        return true;
    }
}
