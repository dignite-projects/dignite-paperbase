using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Converts a raw <see cref="JsonElement"/> — from the LLM, or from an operator edit — into the CLR value
/// the v3 bag stores, rejecting anything that does not match the field type. The v3 successor to
/// <see cref="ExtractedFieldValueValidator"/>, which validated against a <see cref="FieldDataType"/> and
/// left the typed-column split to the entity.
/// <para>
/// Validation and conversion are one step here rather than two, deliberately. Under v2 the validator said
/// yes and <c>DocumentExtractedField.SetValue</c> then did the conversion, so the two had to agree about
/// every type — a duplication that only stayed correct because both switched on the same enum. The bag
/// stores plain CLR values, so whatever decides a value is acceptable is exactly what decides what gets
/// stored, and they cannot drift.
/// </para>
/// <para>
/// Strict, with no coercion: a Number field takes a JSON number, not a numeric string. The field type is
/// a promise about the shape of the value, and quietly accepting "1500.50" for a number would make the
/// promise untrue for anything reading the bag afterwards — including the index, which types each value
/// into a typed column.
/// </para>
/// <para>
/// Both write paths share it, as v2's did: operator edits surface a rejection as a correctable error,
/// while LLM extraction logs it and stores nothing for that field. Normalization belongs in the prompt;
/// this is the last guardrail.
/// </para>
/// </summary>
public static class FlexFieldValueReader
{
    /// <summary>
    /// Attempts to read <paramref name="value"/> as the field type's stored form.
    /// </summary>
    /// <returns>
    /// <c>true</c> with <paramref name="result"/> set when the value is acceptable — including
    /// <c>null</c>, meaning "the document has no value for this field", which is always acceptable.
    /// <c>false</c> when the value does not match the field type.
    /// </returns>
    public static bool TryRead(
        JsonElement value,
        string fieldTypeName,
        FieldConfigurationDictionary configuration,
        out object? result)
    {
        result = null;

        // A field the document does not contain. Distinct from a rejection: the caller stores nothing
        // either way, but only one of the two is worth logging as a problem.
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (string.Equals(fieldTypeName, TagsFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadTags(value, new TagsConfiguration(configuration), out result);
        }

        if (string.Equals(fieldTypeName, SelectFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadSelect(value, new SelectConfiguration(configuration), out result);
        }

        if (string.Equals(fieldTypeName, TextFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadString(value, new TextConfiguration(configuration).CharLimit, out result);
        }

        if (string.Equals(fieldTypeName, CKEditorFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadString(value, DocumentExtractedFieldConsts.MaxLongTextValueLength, out result);
        }

        if (string.Equals(fieldTypeName, NumberFieldType.ControlName, StringComparison.Ordinal))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
            {
                return false;
            }

            result = number;
            return true;
        }

        if (string.Equals(fieldTypeName, BooleanFieldType.ControlName, StringComparison.Ordinal))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            result = value.GetBoolean();
            return true;
        }

        if (string.Equals(fieldTypeName, DateTimeFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadDateTime(value, new DateTimeConfiguration(configuration), out result);
        }

        // An unknown field type is a programming error, not bad data: the value cannot be validated, and
        // storing it unvalidated would put an untyped value into a bag every later reader trusts.
        return false;
    }

    private static bool TryReadString(JsonElement value, int maxLength, out object? result)
    {
        result = null;

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (text == null || text.Length > maxLength)
        {
            return false;
        }

        result = text;
        return true;
    }

    private static bool TryReadTags(JsonElement value, TagsConfiguration configuration, out object? result)
    {
        result = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = element.GetString();
            if (text == null || text.Length > configuration.MaxLength)
            {
                return false;
            }

            items.Add(text);
        }

        // The whole group is rejected rather than truncated: dropping the tail would present a partial
        // extraction as a complete one, which is the failure mode the cap exists to prevent.
        if (items.Count > configuration.MaxCount)
        {
            return false;
        }

        // An empty array is a legitimate "no values", stored as an empty list rather than as absent, so a
        // multi-valued field keeps its shape on the egress.
        result = items;
        return true;
    }

    private static bool TryReadSelect(JsonElement value, SelectConfiguration configuration, out object? result)
    {
        result = null;

        var allowed = configuration.Options
            .Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .Select(o => o.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (configuration.Multiple)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var items = new List<string>();
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var text = element.GetString()!;
                // Enforced even though the extraction schema already emits an enum: the schema constrains
                // the model, this constrains everything - an operator edit, a replayed payload, a provider
                // that ignores the enum.
                if (allowed.Count > 0 && !allowed.Contains(text))
                {
                    return false;
                }

                items.Add(text);
            }

            result = items;
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var single = value.GetString()!;
        if (allowed.Count > 0 && !allowed.Contains(single))
        {
            return false;
        }

        result = single;
        return true;
    }

    private static bool TryReadDateTime(JsonElement value, DateTimeConfiguration configuration, out object? result)
    {
        result = null;

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (configuration.InputMode == DateTimeInputMode.DateTime)
        {
            if (!DateTime.TryParseExact(
                    text, FieldValueFormats.DateTime, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var moment))
            {
                return false;
            }

            result = moment;
            return true;
        }

        // Date mode stores midnight, which is what keeps an equality filter on a date an equality filter
        // now that Date and DateTime share one field type (#559 resolution 5).
        if (!DateOnly.TryParseExact(
                text, FieldValueFormats.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        result = date.ToDateTime(TimeOnly.MinValue);
        return true;
    }
}
