using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>The kernel's boolean field type.</summary>
public class BooleanFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => BooleanFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        result = value.GetBoolean();
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration) => new()
    {
        ["type"] = new JsonArray("boolean", "null"),
        ["description"] = "A JSON boolean, or null when absent."
    };

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.WriteJsonGeneric(value);

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.RenderGeneric(value);

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value, FieldConfigurationDictionary configuration)
        => TryReadBoolean(value, out var flag) ? new[] { flag ? "true" : "false" } : Array.Empty<string>();

    private static bool TryReadBoolean(object value, out bool result)
    {
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                result = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                result = false;
                return true;
            default:
                return bool.TryParse(FieldTypeExtensionHelpers.ReadAsString(value), out result);
        }
    }
}
