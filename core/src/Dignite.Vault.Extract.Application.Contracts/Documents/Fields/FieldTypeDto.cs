namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// What the Angular field designer cannot work out for itself about a registered field type (#562).
/// <para>
/// Deliberately not a mirror of <c>IFieldType</c>. A field type's label is absent because the Angular
/// library already owns labels: each <c>FieldTypeDefinition</c> carries a <c>displayNameKey</c> the
/// client resolves through its own localization, whereas <c>IFieldType.DisplayName</c> is text already
/// localized to the request's culture — shipping both would give the same label two sources. What the
/// client genuinely cannot derive is <see cref="Indexable"/>.
/// </para>
/// </summary>
public class FieldTypeDto
{
    /// <summary>
    /// The registration key — <c>IFieldType.Name</c>, and the value stored in <c>Field.FieldTypeName</c>.
    /// Matches the Angular library's <c>FieldTypeDefinition.name</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether a field of this type can meaningfully be marked <c>IsSearchable</c> — see
    /// <c>FieldTypeExtensions.IsIndexable</c>. The designer disables the setting when this is false;
    /// <see cref="FieldDefinitionDto"/>'s owning app service rejects the combination outright either way.
    /// </summary>
    public bool Indexable { get; set; }
}
