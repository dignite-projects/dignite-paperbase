using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>The kernel's short-text field type.</summary>
public class TextFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => TextFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
        => FieldTypeExtensionHelpers.TryReadString(value, new TextConfiguration(configuration).CharLimit, out result);

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        ["maxLength"] = new TextConfiguration(configuration).CharLimit,
        ["description"] = "A short structured string value, or null when absent."
    };

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.WriteJsonGeneric(value);

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.RenderGeneric(value);

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value)
    {
        var normalized = FieldTypeExtensionHelpers.NormalizeTextForFingerprint(FieldTypeExtensionHelpers.ReadAsString(value));
        return normalized == null ? Array.Empty<string>() : new[] { normalized };
    }
}
