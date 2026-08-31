using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Renders one v3 bag value as the canonical <see cref="JsonElement"/> the <c>ExtractedFields</c> egress
/// carries — the exact inverse of <see cref="FlexFieldValueReader"/>, and the v3 successor to
/// <c>FieldValueFormatter.ToJsonElement</c>.
/// <para>
/// This exists rather than serializing the bag value directly because <c>ExtractedFields</c> is a wire
/// contract that must not shift under the storage change (#560): a v2 <c>Date</c> field emitted
/// <c>"2026-03-14"</c>, and a bag holds it as a midnight <see cref="DateTime"/>, which
/// <c>JsonSerializer</c> would render as <c>"2026-03-14T00:00:00"</c>. Same value, different string, and
/// every downstream consumer parsing that field would see the shape change without a single error.
/// </para>
/// <para>
/// #564: the per-type render now lives on each <see cref="IVaultExtractFieldTypeExtension"/> (see
/// <c>DateTimeFieldTypeExtension</c> for the format-follows-InputMode rule this used to implement inline);
/// this keeps only the multi-value interception that has to run before any type is looked up.
/// </para>
/// </summary>
public static class FlexFieldValueJsonWriter
{
    /// <summary>
    /// Renders <paramref name="value"/> for a field of type <paramref name="fieldTypeName"/>, or
    /// <c>null</c> when the value is absent.
    /// </summary>
    public static JsonElement? Write(
        object? value,
        string fieldTypeName,
        FieldConfigurationDictionary configuration,
        IVaultExtractFieldTypeRegistry registry)
    {
        if (value == null)
        {
            return null;
        }

        // Multi-valued types render as a JSON array of strings, the shape v2's AllowMultiple fields had.
        // Decided by configuration (unlike the fingerprint's runtime-shape check for Select) and
        // intercepted here, before any per-type dispatch: every multi-valued field's elements are plain
        // strings, so no per-type formatting is needed for them.
        if (registry.IsMultiValue(fieldTypeName, configuration))
        {
            return JsonSerializer.SerializeToElement(FieldTypeExtensionHelpers.ReadAsStringList(value));
        }

        return registry.Get(fieldTypeName).WriteJson(value, configuration);
    }
}
