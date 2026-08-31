using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields;

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
/// #564: the per-type scalar render now lives on each <see cref="IVaultExtractFieldTypeExtension"/>; this
/// keeps only the list-vs-scalar shape detection, which is generic and runs before any type is looked up.
/// </para>
/// </summary>
internal static class ExportCellRenderer
{
    public const string MultiValueSeparator = "; ";

    /// <summary>
    /// Renders <paramref name="value"/>, or <c>null</c> for a field this document holds no value for —
    /// an empty cell, never the text "null".
    /// </summary>
    public static string? RenderCell(
        object? value, string fieldTypeName, FieldConfigurationDictionary configuration,
        IVaultExtractFieldTypeRegistry registry)
    {
        if (value == null)
        {
            return null;
        }

        // Loud-fail rather than render by guesswork, carried over from v2's switch: a field type this
        // registry does not know would render a DateTime in some default shape, or a composite value as
        // its type name, into a file an operator hands to an accountant. An error is the better cell.
        if (!registry.IsSupported(fieldTypeName))
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

        return registry.Get(fieldTypeName).RenderForExport(value, configuration);
    }

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
}
