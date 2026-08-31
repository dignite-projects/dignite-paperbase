using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>The kernel's numeric field type.</summary>
public class NumberFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => NumberFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            return false;
        }

        result = number;
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration) => new()
    {
        ["type"] = new JsonArray("number", "null"),
        ["description"] = "A JSON number, or null when absent."
    };

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.WriteJsonGeneric(value);

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.RenderGeneric(value);

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value)
    {
        // Full precision, unlike the export cell's rounded format: two amounts that differ beyond six
        // decimals must not hash to the same fingerprint.
        if (!TryReadDecimal(value, out var number))
        {
            return Array.Empty<string>();
        }

        return new[] { number.ToString(FieldValueFormats.FingerprintNumber, CultureInfo.InvariantCulture) };
    }

    private static bool TryReadDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } e:
                return e.TryGetDecimal(out result);
            case IConvertible:
                try
                {
                    result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
            default:
                result = 0;
                return false;
        }
    }
}
