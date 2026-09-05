using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Table;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields.Tags;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// <see cref="FlexFieldValueSchemaBuilder"/> — the JSON schema each field type holds the model to.
/// </summary>
public class FlexFieldValueSchemaBuilder_Tests
{
    private static JsonObject Build(string fieldTypeName, FieldConfigurationDictionary? configuration = null)
        => FlexFieldValueSchemaBuilder.Build(
            fieldTypeName, configuration ?? new FieldConfigurationDictionary(), TestFieldTypeRegistry.Default);

    private static List<string> Types(JsonObject schema)
        => schema["type"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();

    /// <summary>
    /// Every field must be able to say "not present". Forcing a value trades a missing extraction for an
    /// invented one, which is much harder to notice downstream.
    /// </summary>
    [Theory]
    [InlineData("Text")]
    [InlineData("Number")]
    [InlineData("Boolean")]
    [InlineData("DateTime")]
    [InlineData(CKEditorFieldType.ControlName)]
    [InlineData(TagsFieldType.ControlName)]
    public void Every_field_type_allows_null(string fieldTypeName)
    {
        Types(Build(fieldTypeName)).ShouldContain("null");
    }

    [Fact]
    public void Text_carries_its_configured_char_limit()
    {
        var configuration = new TextConfiguration { CharLimit = 64 }.ConfigurationDictionary;

        Build("Text", configuration)["maxLength"]!.GetValue<int>().ShouldBe(64);
    }

    /// <summary>
    /// The long-text ceiling is an anti-abuse guardrail, not a storage limit: the column is unbounded,
    /// but an untrusted document must not be able to induce an enormous generation.
    /// </summary>
    [Fact]
    public void Long_text_keeps_the_anti_abuse_ceiling()
    {
        Build(CKEditorFieldType.ControlName)["maxLength"]!.GetValue<int>()
            .ShouldBe(DocumentExtractedFieldConsts.MaxLongTextValueLength);
    }

    [Fact]
    public void Tags_bounds_both_the_count_and_each_value()
    {
        var configuration = new TagsConfiguration { MaxCount = 5, MaxLength = 32 }.ConfigurationDictionary;
        var schema = Build(TagsFieldType.ControlName, configuration);

        schema["maxItems"]!.GetValue<int>().ShouldBe(5);
        schema["items"]!["maxLength"]!.GetValue<int>().ShouldBe(32);
    }

    /// <summary>
    /// Date and DateTime are one field type now, so the pattern the model is held to has to come from
    /// configuration. Asking a date-only field for hours would invent precision the document lacks.
    /// </summary>
    [Fact]
    public void Date_and_datetime_get_different_patterns()
    {
        var date = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;
        var dateTime = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        Build("DateTime", date)["pattern"]!.GetValue<string>().ShouldBe(@"^\d{4}-\d{2}-\d{2}$");
        Build("DateTime", dateTime)["pattern"]!.GetValue<string>()
            .ShouldBe(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$");
    }

    /// <summary>
    /// The capability v3 adds: a closed vocabulary becomes a schema-level constraint, so the model
    /// physically cannot return a value outside it. Under v2 this could only be described in the prompt
    /// and checked after the call had already been paid for.
    /// </summary>
    [Fact]
    public void Select_constrains_the_model_to_its_options()
    {
        var configuration = new SelectConfiguration
        {
            Options = new List<SelectListItem>
            {
                new("Draft", "draft", false),
                new("Signed", "signed", false)
            }
        }.ConfigurationDictionary;

        var values = Build(SelectFieldType.ControlName, configuration)["enum"]!.AsArray()
            .Select(v => v?.GetValue<string>())
            .ToList();

        values.ShouldContain("draft");
        values.ShouldContain("signed");
        // Null is in the enum rather than relying on the type union alone: providers read a value enum
        // beside a nullable type inconsistently, and a field the document lacks still needs a way out.
        values.ShouldContain((string?)null);
        values.Count.ShouldBe(3);
    }

    [Fact]
    public void Multi_select_constrains_each_element()
    {
        var configuration = new SelectConfiguration
        {
            Multiple = true,
            Options = new List<SelectListItem> { new("A", "a", false), new("B", "b", false) }
        }.ConfigurationDictionary;

        var schema = Build(SelectFieldType.ControlName, configuration);

        Types(schema).ShouldContain("array");
        schema["items"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .ShouldBe(new[] { "a", "b" });
    }

    /// <summary>
    /// An empty enum would make every value invalid and the field permanently unextractable. Wrong
    /// configuration should degrade to an unconstrained string, not to "this field can never be filled".
    /// </summary>
    [Fact]
    public void Select_without_options_degrades_to_a_plain_string()
    {
        var schema = Build(SelectFieldType.ControlName, new SelectConfiguration().ConfigurationDictionary);

        Types(schema).ShouldContain("string");
        schema["enum"].ShouldBeNull();
    }

    [Fact]
    public void Select_ignores_blank_option_values()
    {
        var configuration = new SelectConfiguration
        {
            Options = new List<SelectListItem> { new("Real", "real", false), new("Blank", "  ", false) }
        }.ConfigurationDictionary;

        Build(SelectFieldType.ControlName, configuration)["enum"]!.AsArray()
            .Select(v => v?.GetValue<string>())
            .ShouldBe(new[] { "real", null });
    }

    /// <summary>
    /// #625: the composite Table type composes an array-of-row-objects schema from its own column
    /// schema, each column's fragment built by that column's own registered extension - pinned through the
    /// shared static entry point, not the extension directly.
    /// </summary>
    [Fact]
    public void Table_composes_a_row_object_schema_from_its_own_columns()
    {
        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        var schema = Build(TableFieldType.ControlName, configuration);

        Types(schema).ShouldContain("array");
        var itemSchema = schema["items"]!.AsObject();
        itemSchema["properties"]!.AsObject().ContainsKey("item").ShouldBeTrue();
        itemSchema["properties"]!.AsObject().ContainsKey("qty").ShouldBeTrue();
        itemSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(new[] { "item" });
    }

    /// <summary>
    /// An unmapped field type would otherwise reach the model as an unconstrained value of unknown shape
    /// and fail validation only after the call. Adding a field type is deliberate, and this is the place
    /// that has to be updated with it.
    /// </summary>
    [Fact]
    public void An_unmapped_field_type_fails_loudly()
    {
        Should.Throw<NotSupportedException>(() => Build("SomeFutureType"));
    }

    [Fact]
    public void Builds_from_a_field_entity()
    {
        var field = new Field(
            Guid.NewGuid(), null, Guid.NewGuid(), "status", "Status", SelectFieldType.ControlName,
            configuration: new SelectConfiguration
            {
                Options = new List<SelectListItem> { new("Draft", "draft", false) }
            }.ConfigurationDictionary);

        FlexFieldValueSchemaBuilder.Build(field, TestFieldTypeRegistry.Default)["enum"]!.AsArray()
            .Select(v => v?.GetValue<string>())
            .ShouldContain("draft");
    }
}
