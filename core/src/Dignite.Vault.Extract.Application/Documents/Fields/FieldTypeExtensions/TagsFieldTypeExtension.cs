using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// Vault Extract's own open-vocabulary multi-value type, the complement of the kernel's closed-vocabulary
/// <c>Select</c>. Always a list.
/// </summary>
public class TagsFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => TagsFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => true;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;
        var tags = new TagsConfiguration(configuration);

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
            if (text == null || text.Length > tags.MaxLength)
            {
                return false;
            }

            items.Add(text);
        }

        // The whole group is rejected rather than truncated: dropping the tail would present a partial
        // extraction as a complete one, which is the failure mode the cap exists to prevent.
        if (items.Count > tags.MaxCount)
        {
            return false;
        }

        // An empty array is a legitimate "no values", stored as an empty list rather than as absent, so a
        // multi-valued field keeps its shape on the egress.
        result = items;
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration)
    {
        var tags = new TagsConfiguration(configuration);
        return new JsonObject
        {
            ["type"] = new JsonArray("array", "null"),
            // maxItems mirrors the validator's hard cap rather than merely hinting at it, so an untrusted
            // document cannot induce an unbounded array that is then rejected wholesale.
            ["maxItems"] = tags.MaxCount,
            ["items"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = tags.MaxLength
            },
            ["description"] = "A JSON array of short structured string values, or null/empty array when absent."
        };
    }

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.WriteJsonGeneric(value);

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.RenderGeneric(value);

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value)
        => FieldTypeExtensionHelpers.CanonicalizeListForFingerprint(FieldTypeExtensionHelpers.ReadAsStringList(value));
}
