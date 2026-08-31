using System;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// Builds the JSON-schema fragment that constrains one field's extracted value, from its v3 field type
/// and configuration — the replacement for the <c>FieldDataType</c> switch in
/// <see cref="FieldExtractionWorkflow"/>.
/// <para>
/// The reason this is worth having beyond parity: a field type can now describe its own value precisely
/// enough for the model to be <i>constrained</i> rather than merely instructed. A <c>Select</c> field emits
/// its configured options as a JSON-schema <c>enum</c>, so the model physically cannot return a value
/// outside the list. Under v2 the only way to express a closed vocabulary was to describe it in the
/// field's prompt and hope — and then reject the mismatch after the call, having already paid for it.
/// </para>
/// <para>
/// Every schema is <c>&lt;type&gt;-or-null</c>: a field the document does not contain must have a way to
/// say so. Forcing a value would trade a missing extraction for an invented one, which is far harder to
/// notice downstream.
/// </para>
/// <para>
/// #564: each type's actual schema fragment lives on its own <see cref="IVaultExtractFieldTypeExtension"/>
/// now — the loud failure for an unregistered type stays here, at the one place a new field type must be
/// wired up before it can reach a document type's schema at all.
/// </para>
/// </summary>
public static class FlexFieldValueSchemaBuilder
{
    /// <summary>
    /// The value schema for <paramref name="field"/>.
    /// </summary>
    public static JsonObject Build(Field field, IVaultExtractFieldTypeRegistry registry)
        => Build(field.FieldTypeName, field.Configuration, registry);

    public static JsonObject Build(
        string fieldTypeName, FieldConfigurationDictionary configuration, IVaultExtractFieldTypeRegistry registry)
    {
        // A field type with no schema here would otherwise reach the model as an unconstrained value of
        // unknown shape, and its output would fail validation after the call rather than before it. Loud
        // failure is the right trade: adding a field type is a deliberate act, and this is the one place
        // that must be updated alongside it.
        if (!registry.TryGet(fieldTypeName, out var extension))
        {
            throw new NotSupportedException(
                $"No extraction schema is defined for field type '{fieldTypeName}'.");
        }

        return extension!.BuildExtractionSchema(configuration);
    }
}
