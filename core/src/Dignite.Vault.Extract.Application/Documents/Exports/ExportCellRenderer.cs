using System;
using System.Linq;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;
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

        // Multi-valued types are rendered here, before any per-type dispatch - exactly the interception
        // FlexFieldValueJsonWriter.Write does, and for the same reason: every multi-valued field's elements
        // are plain strings, so no per-type formatting is needed for them. Decided by registry.IsMultiValue
        // (configuration), never by the value's own runtime shape.
        //
        // This used to be a shape-only check (is the value list-shaped at all, via a switch with a
        // catch-all IEnumerable branch) rather than this registry-first one. That predates
        // IVaultExtractFieldTypeRegistry.IsMultiValue existing as a callable single source of truth at this
        // call site (the check was written in #501, before the v3 registry), and it happened to stay
        // correct by coincidence: every multi-valued field's stored value really was List<string> (Tags
        // always, multi-Select by configuration), and no scalar field's value was ever itself IEnumerable -
        // so "is it list-shaped" and "is it multi-valued" always agreed. Table breaks that coincidence: its
        // List<TableRow> value IS IEnumerable while being one composite scalar (IsMultiValue = false), and
        // the old shape-only check could not tell the two apart - it would have rendered each row via
        // Convert.ToString(row) (a CLR type name), silently, with no error.
        if (registry.IsMultiValue(fieldTypeName, configuration))
        {
            var rendered = FieldTypeExtensionHelpers.ReadAsStringList(value)
                .Where(i => !string.IsNullOrEmpty(i))
                .ToList();
            // The bag preserves list order, so no re-sort: under v2 this had to order by the row's Order
            // column because the database returned the rows in no particular order.
            return rendered.Count > 0 ? string.Join(MultiValueSeparator, rendered) : null;
        }

        return registry.Get(fieldTypeName).RenderForExport(value, configuration);
    }
}
