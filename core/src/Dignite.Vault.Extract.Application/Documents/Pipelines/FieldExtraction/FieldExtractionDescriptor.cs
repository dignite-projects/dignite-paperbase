using System;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// Workflow-internal DTO: the runtime description of one field for extraction, decoupled from the
/// persisted <see cref="Fields.Field"/> entity.
/// <para>
/// One LLM call runs exactly one layer's fields, selected by <c>Document.TenantId</c>, so the descriptor
/// carries no layer marker — the descriptor list itself <i>is</i> a single-layer schema.
/// </para>
/// <para>
/// <see cref="FieldId"/> travels with it because the two identities serve different purposes: LLM output
/// is read back by <see cref="Name"/>, which is the prompt schema's key and the value bag's key, while
/// the in-flight guards match against the immutable id so a rename mid-call cannot be mistaken for a
/// different field.
/// </para>
/// <para>
/// <see cref="FieldTypeName"/> and <see cref="Configuration"/> replace v2's <c>DataType</c> +
/// <c>AllowMultiple</c> pair: under v3 a value's shape, its constraints, and the JSON schema the model is
/// held to all derive from the field type and its configuration.
/// </para>
/// </summary>
public sealed record FieldExtractionDescriptor(
    Guid FieldId,
    string Name,
    string? Prompt,
    string FieldTypeName,
    FieldConfigurationDictionary Configuration,
    bool IsRequired);
