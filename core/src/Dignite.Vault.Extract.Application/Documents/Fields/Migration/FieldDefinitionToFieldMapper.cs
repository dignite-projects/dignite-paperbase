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
/// The #559 resolution-1 v1-dataType -> v3 field-type mapping table, as executable code.
/// <para>
/// Originally written to turn one v2 <c>FieldDefinition</c> row into its v3 <see cref="Field"/>
/// replacement (#561); that entity and its one-shot migrator are gone now that the v3 data migration has
/// run (#593). What is left is the table itself — <see cref="MapType(FieldDataType, bool)"/> and
/// <see cref="IsSearchableFor"/> — which two independent live callers still need: the AI field-drafting
/// assistant (<c>FieldDraftSuggestionAppService</c>, which produces the same v1-shaped
/// <see cref="FieldDataType"/> vocabulary from an LLM response) and the document-type-pack v1 importer
/// (<see cref="DocumentTypePackV1Upconverter"/>, which reads it from a legacy pack file). Both need the
/// exact same "old data type -> v3 field type + configuration" table this class always was, so it stays in
/// place rather than being deleted with the migrator that used to be its third caller.
/// </para>
/// <para>
/// Pure and total — every <see cref="FieldDataType"/> has a target, and an unrecognised one fails loudly
/// rather than defaulting to Text, because a field silently mapped to the wrong type would extract and
/// index wrong values rather than not working at all.
/// </para>
/// </summary>
public static class FieldDefinitionToFieldMapper
{
    /// <summary>
    /// Whether a v1-shaped field should carry <c>IsSearchable = true</c> in v3. v2 indexed every
    /// extracted value unconditionally, so <c>true</c> is the faithful conversion — except for a v3
    /// target type that structurally cannot be indexed (<see cref="CKEditorFieldType"/>, LongText's
    /// target, whose <c>IndexValueType</c> is null). Leaving it <c>true</c> there would collide with the
    /// same searchable/indexable guard that <c>FieldDefinitionAppService</c> and
    /// <c>DocumentTypePackAppService</c> enforce on every other write path. Shared by
    /// <see cref="DocumentTypePackV1Upconverter.Upconvert"/> so the two paths cannot drift apart.
    /// </summary>
    public static bool IsSearchableFor(string fieldTypeName)
        => fieldTypeName != CKEditorFieldType.ControlName;

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
