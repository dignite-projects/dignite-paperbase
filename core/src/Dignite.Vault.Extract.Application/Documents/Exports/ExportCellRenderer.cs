using System;
using System.Collections;
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
using Dignite.Vault.Extract.Documents.Fields;

namespace Dignite.Vault.Extract.Documents.Exports;

/// <summary>
/// Renders one field's value as the string an exported CSV / XLSX cell carries.
/// <para>
/// Presentation, not contract: numbers use <see cref="FieldValueFormats.CellNumber"/>, which trims the six
/// trailing zeros of <c>decimal(38,6)</c> — deliberately unlike the <c>ExtractedFields</c> JSON egress,
/// which keeps the value exact. A cell an accountant reads and a payload a consumer parses want different
/// things from the same number.
/// </para>
/// <para>
/// This is the v3 port of the same dispatch v2 did through <c>FieldValueFormatter.ToCellString</c> keyed
/// on <c>FieldDataType</c>; it belongs with the per-field-type contract tracked on #562, not beside it.
/// </para>
/// </summary>
internal static class ExportCellRenderer
{
    public const string MultiValueSeparator = "; ";

    /// <summary>
    /// Renders <paramref name="value"/>, or <c>null</c> for a field this document holds no value for —
    /// an empty cell, never the text "null".
    /// </summary>
    public static string? RenderCell(object? value, string fieldTypeName, FieldConfigurationDictionary configuration)
    {
        if (value == null)
        {
            return null;
        }

        // Loud-fail rather than render by guesswork, carried over from v2's switch: a field type this method
        // does not know would render a DateTime in some default shape, or a composite value as its type
        // name, into a file an operator hands to an accountant. An error is the better cell.
        if (!IsKnown(fieldTypeName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fieldTypeName), fieldTypeName, "No export cell rendering is defined for this field type.");
        }

        if (TryReadList(value, out var items))
        {
            // The bag preserves list order, so no re-sort: under v2 this had to order by the row's Order
            // column because the database returned the rows in no particular order.
            var rendered = items.Where(i => !string.IsNullOrEmpty(i)).ToList();
            return rendered.Count > 0 ? string.Join(MultiValueSeparator, rendered) : null;
        }

        return RenderScalar(value, fieldTypeName, configuration);
    }

    private static string? RenderScalar(object value, string fieldTypeName, FieldConfigurationDictionary configuration)
    {
        if (string.Equals(fieldTypeName, DateTimeFieldType.ControlName, StringComparison.Ordinal))
        {
            if (!TryReadDateTime(value, out var moment))
            {
                return null;
            }

            var format = DateTimeInputModeFormats.Format(
                new DateTimeConfiguration(configuration).InputMode);
            return moment.ToString(format, CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string s => s,
            decimal d => d.ToString(FieldValueFormats.CellNumber, CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement { ValueKind: JsonValueKind.Number } e =>
                e.GetDecimal().ToString(FieldValueFormats.CellNumber, CultureInfo.InvariantCulture),
            JsonElement { ValueKind: JsonValueKind.True } => "true",
            JsonElement { ValueKind: JsonValueKind.False } => "false",
            JsonElement e => e.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static bool IsKnown(string fieldTypeName)
        => fieldTypeName is TextFieldType.ControlName
            or NumberFieldType.ControlName
            or BooleanFieldType.ControlName
            or DateTimeFieldType.ControlName
            or SelectFieldType.ControlName
            or CKEditorFieldType.ControlName
            or TagsFieldType.ControlName;

    private static bool TryReadList(object value, out List<string> items)
    {
        switch (value)
        {
            case List<string> list:
                items = list;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                items = element.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString())
                    .ToList();
                return true;
            // A string is a sequence of chars; "abc" is one value, not three.
            case string:
                items = new List<string>();
                return false;
            case IEnumerable enumerable:
                items = enumerable.Cast<object?>()
                    .Select(i => Convert.ToString(i, CultureInfo.InvariantCulture) ?? string.Empty).ToList();
                return true;
            default:
                items = new List<string>();
                return false;
        }
    }

    private static bool TryReadDateTime(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt:
                result = dt;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } e:
                return e.TryGetDateTime(out result);
            default:
                return DateTime.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
    }
}
