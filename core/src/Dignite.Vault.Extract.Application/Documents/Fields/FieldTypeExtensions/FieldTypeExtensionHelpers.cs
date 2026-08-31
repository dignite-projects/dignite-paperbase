using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Vault.Extract.Documents.Fields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// Pure shape-normalization shared by more than one <see cref="IVaultExtractFieldTypeExtension"/> - not
/// per-field-type behavior itself, just the CLR/<see cref="JsonElement"/> round-trip every reader/writer
/// in this family has to handle the same way, kept in one place instead of copy-pasted into each extension.
/// </summary>
internal static class FieldTypeExtensionHelpers
{
    /// <summary>
    /// The generic egress render used by every scalar type except <c>DateTime</c> (whose shape depends on
    /// configuration): a <see cref="JsonElement"/> reloaded from the bag is already the right shape; a
    /// fresh in-memory CLR value is serialized once.
    /// </summary>
    public static JsonElement WriteJsonGeneric(object value)
        => value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);

    /// <summary>
    /// The generic export-cell render used by every scalar type except <c>DateTime</c>: a string stays a
    /// string, a decimal is cell-formatted (trimmed, unlike the fingerprint's full-precision format), a
    /// bool is "true"/"false", and a reloaded <see cref="JsonElement"/> normalizes the same way.
    /// </summary>
    public static string? RenderGeneric(object value) => value switch
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

    /// <summary>Reads a JSON string value, rejecting anything else or anything over <paramref name="maxLength"/>.</summary>
    public static bool TryReadString(JsonElement value, int maxLength, out object? result)
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

    /// <summary>Reads a bag value as a plain string, across the CLR-value/reloaded-JsonElement shape split.</summary>
    public static string? ReadAsString(object value) => value switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Fingerprint text normalization: folds whitespace runs to one space, trims, lower-cases invariantly,
    /// so "INV 001" and "inv  001" are the same key. Null/blank normalizes to "no usable value" (null), the
    /// signal that makes an owning unique key partial.
    /// </summary>
    public static string? NormalizeTextForFingerprint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var folded = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return folded.ToLowerInvariant();
    }

    /// <summary>
    /// Reads a bag value as a list of strings, across every shape a multi-valued field's value can arrive
    /// in: a fresh in-memory <see cref="List{String}"/>, a reloaded JSON array, or (defensively) any other
    /// enumerable. A bare string is one value, never a sequence of characters.
    /// </summary>
    public static List<string> ReadAsStringList(object value) => value switch
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

    /// <summary>
    /// Normalizes every element of a multi-valued fingerprint contribution, in bag order. Returns an empty
    /// list - a partial key - the instant any element fails to normalize: a multi-valued key whose second
    /// element is blank is a partial key, not a shorter one, or two documents with different data could
    /// hash identically.
    /// </summary>
    public static IReadOnlyList<string> CanonicalizeListForFingerprint(IReadOnlyList<string> raw)
    {
        if (raw.Count == 0)
        {
            return Array.Empty<string>();
        }

        var canonical = new List<string>(raw.Count);
        foreach (var element in raw)
        {
            var normalized = NormalizeTextForFingerprint(element);
            if (normalized == null)
            {
                return Array.Empty<string>();
            }

            canonical.Add(normalized);
        }

        return canonical;
    }

    /// <summary>Appends a literal JSON <c>null</c> to a fresh copy of <paramref name="options"/>, for a closed-vocabulary schema's "or absent" case.</summary>
    public static JsonArray WithNullOption(JsonArray options)
    {
        var withNull = options.DeepClone().AsArray();
        withNull.Add(null);
        return withNull;
    }
}
