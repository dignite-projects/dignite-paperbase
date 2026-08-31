using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// The kernel's rich/long-text field type, carrying v2's <c>LongText</c>. Never indexed (the kernel's own
/// <c>IndexValueType</c> is null for this type), so "never indexed, never queryable" is structural rather
/// than dependent on a field's <c>IsSearchable</c> flag.
/// </summary>
public class CKEditorFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    public override string FieldTypeName => CKEditorFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
        => FieldTypeExtensionHelpers.TryReadString(value, DocumentExtractedFieldConsts.MaxLongTextValueLength, out result);

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        // An anti-abuse ceiling, not a storage limit: the column is unbounded, but an untrusted document
        // must not be able to induce an enormous generation.
        ["maxLength"] = DocumentExtractedFieldConsts.MaxLongTextValueLength,
        ["description"] = "A long-form text value (e.g. a summary or description), or null when absent."
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
