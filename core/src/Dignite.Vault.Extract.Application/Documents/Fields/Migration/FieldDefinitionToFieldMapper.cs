using System;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Fields.Migration;

/// <summary>
/// The #559 resolution-1 mapping table, as executable code: one v2 <see cref="FieldDefinition"/> to the
/// v3 <see cref="Field"/> that replaces it.
/// <para>
/// Pure and total — every <see cref="FieldDataType"/> has a target, and an unrecognised one fails loudly
/// rather than defaulting to Text, because a field silently mapped to the wrong type would extract and
/// index wrong values rather than not working at all.
/// </para>
/// <para>
/// <b>The <see cref="FieldDefinition.Id"/> is preserved.</b> That is what keeps
/// <c>DocumentFieldValidationWarning</c> rows and every derived index row valid without a second
/// remapping pass, and it is why the migration can be re-run: a definition already migrated is
/// recognisable by its id.
/// </para>
/// </summary>
public static class FieldDefinitionToFieldMapper
{
    /// <summary>
    /// Builds the v3 <see cref="Field"/> for <paramref name="definition"/>.
    /// <para>
    /// <c>IsSearchable</c> defaults to <c>true</c>: v2 indexed every extracted value unconditionally, with
    /// no opt-out, so that is the faithful conversion for every v3 target type that can actually be
    /// indexed. See <see cref="IsSearchableFor"/> for the one exception.
    /// </para>
    /// </summary>
    public static Field Map(FieldDefinition definition)
    {
        var (fieldTypeName, configuration) = MapType(definition);

        return new Field(
            id: definition.Id,
            tenantId: definition.TenantId,
            documentTypeId: definition.DocumentTypeId,
            name: definition.Name,
            displayName: definition.DisplayName,
            fieldTypeName: fieldTypeName,
            // v2's Prompt is v3's Description - the kernel's consumers already treat Description as the
            // field's AI-facing briefing (#559 resolution 4 rationale).
            description: definition.Prompt,
            configuration: configuration,
            displayOrder: definition.DisplayOrder,
            isRequired: definition.IsRequired,
            isSearchable: IsSearchableFor(fieldTypeName),
            isUniqueKey: definition.IsUniqueKey);
    }

    /// <summary>
    /// Whether a v2-sourced field should carry <c>IsSearchable = true</c> in v3. v2 indexed every
    /// extracted value unconditionally, so <c>true</c> is the faithful conversion — except for a v3
    /// target type that structurally cannot be indexed (<see cref="CKEditorFieldType"/>, LongText's
    /// target, whose <c>IndexValueType</c> is null). Leaving it <c>true</c> there would collide with the
    /// same searchable/indexable guard that <c>FieldDefinitionAppService</c> and
    /// <c>DocumentTypePackAppService</c> enforce on every other write path, and fail the very migration
    /// or pack import meant to carry the field forward. Shared by <see cref="Map"/> and
    /// <see cref="DocumentTypePackV1Upconverter.Upconvert"/> so the two paths cannot drift apart.
    /// </summary>
    public static bool IsSearchableFor(string fieldTypeName)
        => fieldTypeName != CKEditorFieldType.ControlName;

    /// <summary>
    /// Resolves the field type and its configuration. Exposed separately so the pack upconverter, which
    /// has a DTO rather than an entity, can share exactly this table instead of restating it.
    /// </summary>
    public static (string FieldTypeName, FieldConfigurationDictionary Configuration) MapType(
        FieldDefinition definition)
    {
        return MapType(definition.DataType, definition.AllowMultiple);
    }

    public static (string FieldTypeName, FieldConfigurationDictionary Configuration) MapType(
        FieldDataType dataType,
        bool allowMultiple)
    {
        // Multi-value was only ever legal on Text (v2's ValidateMultiValue loud-failed for every other
        // type), and in v3 "is this multi-valued" is a property of the type itself rather than a flag
        // beside it - so a multi-valued text field becomes Tags, the open-vocabulary type. Not Select:
        // Select validates against a configured option list, and a migrated field has no such list.
        if (allowMultiple)
        {
            if (dataType != FieldDataType.Text)
            {
                throw new ArgumentException(
                    $"AllowMultiple is set on a {dataType} field, which v2 could not have produced. " +
                    "Refusing to guess a v3 field type for it.",
                    nameof(dataType));
            }

            return (TagsFieldType.ControlName, new TagsConfiguration
            {
                MaxCount = DocumentExtractedFieldConsts.MaxMultiValueCount,
                MaxLength = DocumentExtractedFieldConsts.MaxTextValueLength
            }.ConfigurationDictionary);
        }

        switch (dataType)
        {
            case FieldDataType.Text:
                return (TextFieldType.ControlName, new TextConfiguration
                {
                    Mode = TextMode.SingleLine,
                    CharLimit = DocumentExtractedFieldConsts.MaxTextValueLength
                }.ConfigurationDictionary);

            case FieldDataType.Number:
                return (NumberFieldType.ControlName, new FieldConfigurationDictionary());

            case FieldDataType.Boolean:
                return (BooleanFieldType.ControlName, new FieldConfigurationDictionary());

            // Date and DateTime share one field type and are told apart by InputMode (#559 resolution 5).
            // The distinction is not lost, it moves from the type enum into the type's configuration.
            case FieldDataType.Date:
                return (DateTimeFieldType.ControlName, new DateTimeConfiguration
                {
                    InputMode = DateTimeInputMode.Date
                }.ConfigurationDictionary);

            case FieldDataType.DateTime:
                return (DateTimeFieldType.ControlName, new DateTimeConfiguration
                {
                    InputMode = DateTimeInputMode.DateTime
                }.ConfigurationDictionary);

            // LongText -> CKEditor, whose IndexValueType is null, so "never indexed, never queryable"
            // stays a structural guarantee instead of depending on IsSearchable being left off.
            // ContentFormat must be set explicitly: the type defaults to Html, and these values are
            // plain text / Markdown extracted from a document, never HTML.
            case FieldDataType.LongText:
                return (CKEditorFieldType.ControlName, new CKEditorConfiguration
                {
                    ContentFormat = CKEditorContentFormat.Markdown,
                    Mode = CKEditorMode.Basic
                }.ConfigurationDictionary);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dataType), dataType, "No v3 field type is defined for this v2 data type.");
        }
    }
}
