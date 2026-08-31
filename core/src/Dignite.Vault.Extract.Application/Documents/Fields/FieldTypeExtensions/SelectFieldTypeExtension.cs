using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Select;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// The kernel's closed-vocabulary field type. <see cref="SelectConfiguration.Options"/> is projected into
/// the LLM extraction schema as a JSON-schema <c>enum</c>. Multi-valued when
/// <see cref="SelectConfiguration.Multiple"/> is set.
/// </summary>
public class SelectFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => SelectFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration)
        => configuration != null && new SelectConfiguration(configuration).Multiple;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;
        var select = new SelectConfiguration(configuration);

        var allowed = select.Options
            .Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .Select(o => o.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (select.Multiple)
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
                // the model, this constrains everything else - an operator edit, a replayed payload, a
                // provider that ignores the enum. Membership is required unconditionally, including when
                // the option list is empty, matching the kernel's own SelectFieldType.Validate.
                if (!allowed.Contains(text))
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
        if (!allowed.Contains(single))
        {
            return false;
        }

        result = single;
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration)
    {
        var select = new SelectConfiguration(configuration);

        var options = new JsonArray();
        foreach (var option in select.Options.Where(o => !string.IsNullOrWhiteSpace(o.Value)))
        {
            options.Add(option.Value);
        }

        // No options configured means no closed vocabulary to enforce. Emitting an empty enum would make
        // every value invalid and the field permanently unextractable, so this degrades to a plain string
        // instead - wrong configuration should not silently become "this field can never have a value".
        if (options.Count == 0)
        {
            return new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["description"] = "A string value, or null when absent."
            };
        }

        if (select.Multiple)
        {
            return new JsonObject
            {
                ["type"] = new JsonArray("array", "null"),
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = options.DeepClone()
                },
                ["description"] = "A JSON array of values drawn from the allowed list, or null/empty array when absent."
            };
        }

        return new JsonObject
        {
            // The enum carries the null option itself: a "string-or-null" type alongside a value enum is
            // read inconsistently across providers, and the whole point of this branch is that the model
            // cannot return anything else.
            ["type"] = new JsonArray("string", "null"),
            ["enum"] = FieldTypeExtensionHelpers.WithNullOption(options),
            ["description"] = "One value from the allowed list, or null when absent."
        };
    }

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.WriteJsonGeneric(value);

    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => FieldTypeExtensionHelpers.RenderGeneric(value);

    /// <summary>
    /// Routed by the value's own runtime shape rather than by configuration: the reader only ever stores a
    /// list for a multi-Select and a bare string for a single one, and that keeps a multi-Select unique key
    /// from silently falling through to the scalar path and nulling the whole fingerprint.
    /// </summary>
    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value)
    {
        if (IsListShaped(value))
        {
            return FieldTypeExtensionHelpers.CanonicalizeListForFingerprint(
                FieldTypeExtensionHelpers.ReadAsStringList(value));
        }

        var normalized = FieldTypeExtensionHelpers.NormalizeTextForFingerprint(FieldTypeExtensionHelpers.ReadAsString(value));
        return normalized == null ? Array.Empty<string>() : new[] { normalized };
    }

    private static bool IsListShaped(object value)
        => value is JsonElement { ValueKind: JsonValueKind.Array } or IEnumerable<string> or IList;
}
