using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// Converts a raw <see cref="JsonElement"/> — from the LLM, or from an operator edit — into the CLR value
/// the v3 bag stores, rejecting anything that does not match the field type. The v3 successor to v2's
/// now-removed <c>ExtractedFieldValueValidator</c> (#593), which validated against a
/// <see cref="FieldDataType"/> and left the typed-column split to the entity.
/// <para>
/// Validation and conversion are one step here rather than two, deliberately. Under v2 the validator said
/// yes and <c>DocumentExtractedField.SetValue</c> then did the conversion, so the two had to agree about
/// every type — a duplication that only stayed correct because both switched on the same enum. The bag
/// stores plain CLR values, so whatever decides a value is acceptable is exactly what decides what gets
/// stored, and they cannot drift.
/// </para>
/// <para>
/// Both write paths share it, as v2's did: operator edits surface a rejection as a correctable error,
/// while LLM extraction logs it and stores nothing for that field. Normalization belongs in the prompt;
/// this is the last guardrail.
/// </para>
/// <para>
/// #564: the per-field-type strictness/no-coercion rules themselves live on each
/// <see cref="IVaultExtractFieldTypeExtension"/> now — this only owns the "field absent" short-circuit that
/// applies before any type is even looked up, and the lookup itself.
/// </para>
/// </summary>
public static class FlexFieldValueReader
{
    /// <summary>
    /// Attempts to read <paramref name="value"/> as the field type's stored form.
    /// </summary>
    /// <returns>
    /// <c>true</c> with <paramref name="result"/> set when the value is acceptable — including
    /// <c>null</c>, meaning "the document has no value for this field", which is always acceptable.
    /// <c>false</c> when the value does not match the field type.
    /// </returns>
    public static bool TryRead(
        JsonElement value,
        string fieldTypeName,
        FieldConfigurationDictionary configuration,
        IVaultExtractFieldTypeRegistry registry,
        out object? result)
    {
        result = null;

        // A field the document does not contain. Distinct from a rejection: the caller stores nothing
        // either way, but only one of the two is worth logging as a problem.
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        // An unknown field type is a programming error, not bad data: the value cannot be validated, and
        // storing it unvalidated would put an untyped value into a bag every later reader trusts.
        return registry.TryGet(fieldTypeName, out var extension)
               && extension!.TryRead(value, configuration, out result);
    }
}
