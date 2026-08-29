using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// <see cref="Document.FieldFingerprint"/> (#411) computed from the v3 value bag — the replacement for
/// <see cref="FieldFingerprintCalculator"/>, which reads the v2 child rows.
/// <para>
/// <b>The fingerprint is a stored hash compared by string equality, so the canonical string this builds
/// is a contract, not an implementation detail.</b> Change how a value is normalized and every document
/// hashed under the old rule stops matching documents hashed under the new one — the corpus silently
/// splits into two populations that can never be duplicates of each other. That is why the migration
/// recomputes every fingerprint rather than trying to preserve equality across the storage change (#561):
/// preserving it byte-for-byte would have frozen the v2 row layout as a permanent constraint on this file.
/// </para>
/// <para>
/// The determinism rules are carried over unchanged from v2, because they are what make the hash stable
/// rather than merely reproducible on one machine:
/// <list type="bullet">
///   <item>unique-key fields in <see cref="Field.Id"/> order, and each field's values in bag order, so
///   enumeration order never leaks into the hash;</item>
///   <item>values normalized per field type — text trimmed, whitespace folded, lower-cased; numbers with
///   trailing zeros stripped; dates in an invariant ISO form — so two scans of the same document that
///   differ only cosmetically still match;</item>
///   <item>a <b>partial key</b> (a declared unique-key field with no usable value) returns
///   <c>null</c>, because an incomplete key would collide unrelated documents.</item>
/// </list>
/// </para>
/// </summary>
public static class FlexFieldFingerprintCalculator
{
    // Same separators as v2: ASCII unit/record separators, which cannot appear in a normalized value, so
    // distinct field and value boundaries can never alias into the same canonical string.
    private const char ValueSeparator = '';
    private const char FieldSeparator = '';

    /// <summary>
    /// Returns the fingerprint for <paramref name="document"/>, or <c>null</c> when its type declares no
    /// unique-key fields or the key is partial.
    /// </summary>
    /// <param name="document">The document whose bag is hashed.</param>
    /// <param name="definitions">
    /// The document type's current fields. Only <see cref="Field.IsUniqueKey"/> ones participate.
    /// </param>
    public static string? Compute(IHasFlexFields document, IReadOnlyCollection<Field> definitions)
    {
        var uniqueKeyFields = definitions
            .Where(d => d.IsUniqueKey)
            .OrderBy(d => d.Id)
            .ToList();

        if (uniqueKeyFields.Count == 0)
        {
            // Duplicate detection is opt-in per type.
            return null;
        }

        var builder = new StringBuilder();
        foreach (var field in uniqueKeyFields)
        {
            var canonicalValues = Canonicalize(document.GetField(field.Name), field.FieldTypeName);

            // No value, or a value that normalizes to nothing, both make the key partial.
            if (canonicalValues.Count == 0)
            {
                return null;
            }

            builder.Append(field.Id.ToString("N"));
            builder.Append('=');
            for (var i = 0; i < canonicalValues.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(ValueSeparator);
                }

                builder.Append(canonicalValues[i]);
            }

            builder.Append(FieldSeparator);
        }

        return ContentHasher.Sha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// Normalizes one bag value into the zero, one, or many canonical strings it contributes.
    /// <para>
    /// Returns an empty list when any part of the value is unusable, rather than skipping it: a
    /// multi-valued key whose second element is blank is a partial key, not a shorter key. Silently
    /// dropping it would let two documents with different data hash identically.
    /// </para>
    /// </summary>
    private static List<string> Canonicalize(object? value, string fieldTypeName)
    {
        if (value == null)
        {
            return new List<string>();
        }

        // Multi-valued: every element must normalize, in bag order.
        if (string.Equals(fieldTypeName, TagsFieldType.ControlName, StringComparison.Ordinal))
        {
            var elements = ReadList(value);
            if (elements.Count == 0)
            {
                return new List<string>();
            }

            var canonical = new List<string>(elements.Count);
            foreach (var element in elements)
            {
                var normalized = NormalizeText(element);
                if (normalized == null)
                {
                    return new List<string>();
                }

                canonical.Add(normalized);
            }

            return canonical;
        }

        var single = CanonicalizeScalar(value, fieldTypeName);
        return single == null ? new List<string>() : new List<string> { single };
    }

    private static string? CanonicalizeScalar(object value, string fieldTypeName)
    {
        if (string.Equals(fieldTypeName, TextFieldType.ControlName, StringComparison.Ordinal) ||
            string.Equals(fieldTypeName, CKEditorFieldType.ControlName, StringComparison.Ordinal))
        {
            return NormalizeText(ReadString(value));
        }

        if (string.Equals(fieldTypeName, NumberFieldType.ControlName, StringComparison.Ordinal))
        {
            // Full precision on purpose, unlike the export's rounded cell format: two amounts that differ
            // beyond six decimals must not hash to the same fingerprint.
            return TryReadDecimal(value, out var number)
                ? number.ToString("0.############################", CultureInfo.InvariantCulture)
                : null;
        }

        if (string.Equals(fieldTypeName, BooleanFieldType.ControlName, StringComparison.Ordinal))
        {
            return TryReadBoolean(value, out var flag) ? (flag ? "true" : "false") : null;
        }

        if (string.Equals(fieldTypeName, DateTimeFieldType.ControlName, StringComparison.Ordinal))
        {
            if (!TryReadDateTime(value, out var moment))
            {
                return null;
            }

            // One format for both input modes, unlike v2's separate Date / DateTime branches. A Date-mode
            // value is stored at midnight, so this renders it as "…T00:00:00" - stable, and distinct from
            // a DateTime-mode value at any other time of day.
            return moment.ToString(FieldValueFormats.DateTime, CultureInfo.InvariantCulture);
        }

        // An unrecognized field type contributes nothing, which makes the key partial rather than hashing
        // an arbitrary ToString(). A field type this calculator does not understand must not silently
        // produce a fingerprint that a later version would compute differently.
        return null;
    }

    private static string? NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Fold whitespace runs to one space, trim, lower-case invariantly, so "INV 001" and "inv  001"
        // are the same key. Identical to v2's rule.
        var folded = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return folded.ToLowerInvariant();
    }

    // --- bag readers ---
    //
    // A value bag round-trips through JSON, so what comes back depends on who put it back: an in-memory
    // entity holds the CLR value the writer set, while a reloaded one holds a JsonElement. Both shapes
    // have to normalize identically, or a document would fingerprint differently before and after a
    // reload - the single most confusing failure this component could have.

    private static string? ReadString(object value) => value switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static bool TryReadDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } e: return e.TryGetDecimal(out result);
            case IConvertible:
                try { result = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return true; }
                catch { result = 0; return false; }
            default: result = 0; return false;
        }
    }

    private static bool TryReadBoolean(object value, out bool result)
    {
        switch (value)
        {
            case bool b: result = b; return true;
            case JsonElement { ValueKind: JsonValueKind.True }: result = true; return true;
            case JsonElement { ValueKind: JsonValueKind.False }: result = false; return true;
            default: return bool.TryParse(ReadString(value), out result);
        }
    }

    private static bool TryReadDateTime(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt: result = dt; return true;
            case JsonElement { ValueKind: JsonValueKind.String } e: return e.TryGetDateTime(out result);
            default:
                return DateTime.TryParse(
                    ReadString(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
    }

    private static List<string> ReadList(object value)
    {
        switch (value)
        {
            case List<string> list:
                return list;
            // Before IEnumerable: a string is a sequence of chars, and "abc" is one value, not three.
            case string single:
                return new List<string> { single };
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                return element.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString())
                    .ToList();
            case JsonElement element:
                return new List<string> { element.ValueKind == JsonValueKind.String ? element.GetString()! : element.ToString() };
            case IEnumerable items:
                return items.Cast<object?>()
                    .Select(i => Convert.ToString(i, CultureInfo.InvariantCulture) ?? string.Empty)
                    .ToList();
            default:
                return new List<string> { Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty };
        }
    }
}
