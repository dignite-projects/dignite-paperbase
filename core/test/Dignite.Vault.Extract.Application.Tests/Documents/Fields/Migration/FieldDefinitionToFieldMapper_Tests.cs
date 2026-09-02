using System;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Fields.Migration;
using Dignite.Vault.Extract.FlexFields.Tags;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Fields.Migration;

/// <summary>
/// The v2 -> v3 field-type mapping (#559 resolution 1, #561 step 3). Every case is asserted because a
/// wrong mapping does not fail: the field keeps working and extracts or indexes the wrong shape.
/// </summary>
public class FieldDefinitionToFieldMapper_Tests
{
    private static FieldDefinition Definition(
        FieldDataType dataType,
        bool allowMultiple = false,
        string? prompt = "extract it",
        bool isRequired = false,
        bool isUniqueKey = false,
        int displayOrder = 0)
    {
        return new FieldDefinition(
            Guid.NewGuid(), tenantId: null, documentTypeId: Guid.NewGuid(),
            name: "some_field", displayName: "Some Field", prompt: prompt, dataType: dataType,
            displayOrder: displayOrder, isRequired: isRequired, allowMultiple: allowMultiple,
            isUniqueKey: isUniqueKey);
    }

    /// <summary>
    /// Preserving the id is what keeps DocumentFieldValidationWarning rows and every derived index row
    /// valid without a second remapping pass, and what makes the migration re-runnable.
    /// </summary>
    [Fact]
    public void Preserves_identity_and_every_carried_property()
    {
        var definition = Definition(FieldDataType.Number, isRequired: true, isUniqueKey: true, displayOrder: 7);

        var field = FieldDefinitionToFieldMapper.Map(definition);

        field.Id.ShouldBe(definition.Id);
        field.TenantId.ShouldBe(definition.TenantId);
        field.DocumentTypeId.ShouldBe(definition.DocumentTypeId);
        field.Name.ShouldBe(definition.Name);
        field.DisplayName.ShouldBe(definition.DisplayName);
        field.DisplayOrder.ShouldBe(7);
        field.IsRequired.ShouldBeTrue();
        field.IsUniqueKey.ShouldBeTrue();
    }

    /// <summary>v2's Prompt becomes v3's Description - the field's AI-facing briefing.</summary>
    [Fact]
    public void Moves_the_prompt_onto_description()
    {
        FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Text, prompt: "The total contract value."))
            .Description.ShouldBe("The total contract value.");

        FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Text, prompt: null))
            .Description.ShouldBeNull();
    }

    /// <summary>
    /// v2 indexed every extracted value with no opt-out, so anything but true here would silently narrow
    /// what a migrated deployment can filter on - for every v3 target type that can actually be indexed.
    /// </summary>
    [Fact]
    public void Migrated_fields_stay_searchable()
    {
        FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Text)).IsSearchable.ShouldBeTrue();
    }

    /// <summary>
    /// LongText's target type, CKEditor, cannot be indexed (IndexValueType is null) - Map must not carry
    /// v2's unconditional IsSearchable=true onto it. It used to: FieldDefinitionAppService.CheckSearchable
    /// rejects exactly this combination, so an already-migrated LongText field would fail the very
    /// rebuild-index / pack-export round trip meant to carry it forward, and a version-1 pack containing
    /// one would fail ImportFieldsAsync outright (DocumentTypePackV1Upconverter shares this same table).
    /// </summary>
    [Fact]
    public void Long_text_is_migrated_as_not_searchable()
    {
        FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.LongText)).IsSearchable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FieldDataType.Text, TextFieldType.ControlName)]
    [InlineData(FieldDataType.Number, NumberFieldType.ControlName)]
    [InlineData(FieldDataType.Boolean, BooleanFieldType.ControlName)]
    [InlineData(FieldDataType.Date, DateTimeFieldType.ControlName)]
    [InlineData(FieldDataType.DateTime, DateTimeFieldType.ControlName)]
    [InlineData(FieldDataType.LongText, CKEditorFieldType.ControlName)]
    public void Maps_each_data_type_to_its_field_type(FieldDataType dataType, string expected)
    {
        FieldDefinitionToFieldMapper.Map(Definition(dataType)).FieldTypeName.ShouldBe(expected);
    }

    /// <summary>
    /// Date and DateTime share a field type, so the distinction has to survive in configuration or it is
    /// genuinely lost - a Date field would start inviting hours and minutes it never had.
    /// </summary>
    [Fact]
    public void Date_and_datetime_are_told_apart_by_input_mode()
    {
        var date = new DateTimeConfiguration(
            FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Date)).Configuration);
        var dateTime = new DateTimeConfiguration(
            FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.DateTime)).Configuration);

        date.InputMode.ShouldBe(DateTimeInputMode.Date);
        dateTime.InputMode.ShouldBe(DateTimeInputMode.DateTime);
    }

    /// <summary>
    /// CKEditor defaults to Html, and these values are plain text or Markdown extracted from a document.
    /// Relying on the default would store them under a format they are not.
    /// </summary>
    [Fact]
    public void Long_text_is_migrated_as_markdown_not_html()
    {
        var configuration = new CKEditorConfiguration(
            FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.LongText)).Configuration);

        configuration.ContentFormat.ShouldBe(CKEditorContentFormat.Markdown);
    }

    /// <summary>
    /// Multi-valued text becomes Tags, not Select: Select validates against a configured option list, and
    /// a migrated field has none - every value it already holds would fail validation.
    /// </summary>
    [Fact]
    public void Multi_valued_text_becomes_tags_carrying_the_v2_limits()
    {
        var field = FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Text, allowMultiple: true));

        field.FieldTypeName.ShouldBe(TagsFieldType.ControlName);

        var configuration = new TagsConfiguration(field.Configuration);
        configuration.MaxCount.ShouldBe(DocumentExtractedFieldConsts.MaxMultiValueCount);
        configuration.MaxLength.ShouldBe(DocumentExtractedFieldConsts.MaxTextValueLength);
    }

    /// <summary>
    /// Single-valued text keeps v2's 256-character ceiling as its CharLimit, so a migrated field does not
    /// quietly start accepting values its index slot cannot hold.
    /// </summary>
    [Fact]
    public void Single_valued_text_keeps_the_v2_length_ceiling()
    {
        var configuration = new TextConfiguration(
            FieldDefinitionToFieldMapper.Map(Definition(FieldDataType.Text)).Configuration);

        configuration.CharLimit.ShouldBe(DocumentExtractedFieldConsts.MaxTextValueLength);
        configuration.Mode.ShouldBe(TextMode.SingleLine);
    }

    /// <summary>
    /// A shape v2 could not have produced (its own ValidateMultiValue refused it) must fail loudly rather
    /// than be mapped to a guess.
    /// </summary>
    [Fact]
    public void Refuses_to_guess_for_a_shape_v2_could_not_produce()
    {
        Should.Throw<ArgumentException>(() =>
            FieldDefinitionToFieldMapper.MapType(FieldDataType.Number, allowMultiple: true));
    }
}
