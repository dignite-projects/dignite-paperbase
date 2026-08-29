using System;
using System.Collections.Generic;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields.Tags;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// <see cref="FlexFieldValueReader"/> — the last guardrail between untrusted extraction output and the
/// value bag every later reader trusts.
/// </summary>
public class FlexFieldValueReader_Tests
{
    private static JsonElement Json(string raw)
        => JsonSerializer.Deserialize<JsonElement>(raw);

    private static bool TryRead(
        string json, string fieldTypeName, out object? result, FieldConfigurationDictionary? configuration = null)
        => FlexFieldValueReader.TryRead(
            Json(json), fieldTypeName, configuration ?? new FieldConfigurationDictionary(), out result);

    /// <summary>
    /// A field the document does not contain is accepted with no value — distinct from a rejection, even
    /// though the caller stores nothing either way, because only one of the two is a problem worth
    /// logging.
    /// </summary>
    [Fact]
    public void Null_is_accepted_as_absent()
    {
        TryRead("null", "Text", out var result).ShouldBeTrue();
        result.ShouldBeNull();
    }

    [Fact]
    public void Text_reads_as_a_string()
    {
        TryRead("\"INV-001\"", "Text", out var result).ShouldBeTrue();
        result.ShouldBe("INV-001");
    }

    [Fact]
    public void Text_over_its_char_limit_is_rejected()
    {
        var configuration = new TextConfiguration { CharLimit = 5 }.ConfigurationDictionary;

        TryRead("\"123456\"", "Text", out _, configuration).ShouldBeFalse();
        TryRead("\"12345\"", "Text", out _, configuration).ShouldBeTrue();
    }

    /// <summary>
    /// No coercion: the field type is a promise about the value's shape, and accepting a numeric string
    /// would make that promise untrue for the index, which types each value into a typed column.
    /// </summary>
    [Theory]
    [InlineData("\"1500.50\"")]
    [InlineData("true")]
    [InlineData("[1]")]
    public void Number_rejects_anything_that_is_not_a_json_number(string json)
    {
        TryRead(json, "Number", out _).ShouldBeFalse();
    }

    [Fact]
    public void Number_reads_as_decimal()
    {
        TryRead("1500.50", "Number", out var result).ShouldBeTrue();
        result.ShouldBe(1500.50m);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void Boolean_rejects_anything_that_is_not_a_json_boolean(string json)
    {
        TryRead(json, "Boolean", out _).ShouldBeFalse();
    }

    [Fact]
    public void Boolean_reads_as_bool()
    {
        TryRead("true", "Boolean", out var result).ShouldBeTrue();
        result.ShouldBe(true);
    }

    /// <summary>
    /// Date mode stores midnight, which is what keeps an equality filter on a date an equality filter now
    /// that Date and DateTime share one field type.
    /// </summary>
    [Fact]
    public void Date_mode_reads_midnight()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;

        TryRead("\"2026-03-14\"", "DateTime", out var result, configuration).ShouldBeTrue();
        result.ShouldBe(new DateTime(2026, 3, 14, 0, 0, 0));
    }

    [Fact]
    public void Date_mode_rejects_a_datetime_string()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;

        TryRead("\"2026-03-14T10:30:00\"", "DateTime", out _, configuration).ShouldBeFalse();
    }

    [Fact]
    public void Datetime_mode_reads_the_full_moment()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        TryRead("\"2026-03-14T10:30:00\"", "DateTime", out var result, configuration).ShouldBeTrue();
        result.ShouldBe(new DateTime(2026, 3, 14, 10, 30, 0));
    }

    [Fact]
    public void Tags_reads_a_string_array()
    {
        TryRead("[\"Acme\",\"Globex\"]", TagsFieldType.ControlName, out var result).ShouldBeTrue();
        result.ShouldBeOfType<List<string>>().ShouldBe(new[] { "Acme", "Globex" });
    }

    /// <summary>
    /// An empty array is a legitimate "no values", stored as an empty list rather than as absent, so a
    /// multi-valued field keeps its shape on the egress.
    /// </summary>
    [Fact]
    public void Tags_keeps_an_empty_array_as_an_empty_list()
    {
        TryRead("[]", TagsFieldType.ControlName, out var result).ShouldBeTrue();
        result.ShouldBeOfType<List<string>>().ShouldBeEmpty();
    }

    /// <summary>
    /// The whole group is rejected rather than truncated: dropping the tail would present a partial
    /// extraction as a complete one, which is the failure the cap exists to prevent.
    /// </summary>
    [Fact]
    public void Tags_over_the_count_cap_is_rejected_whole()
    {
        var configuration = new TagsConfiguration { MaxCount = 2 }.ConfigurationDictionary;

        TryRead("[\"a\",\"b\",\"c\"]", TagsFieldType.ControlName, out _, configuration).ShouldBeFalse();
        TryRead("[\"a\",\"b\"]", TagsFieldType.ControlName, out _, configuration).ShouldBeTrue();
    }

    [Fact]
    public void Tags_rejects_a_non_string_element()
    {
        TryRead("[\"a\",1]", TagsFieldType.ControlName, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Enforced here as well as in the extraction schema: the schema constrains the model, this constrains
    /// everything else — an operator edit, a replayed payload, a provider that ignores the enum.
    /// </summary>
    [Fact]
    public void Select_rejects_a_value_outside_its_options()
    {
        var configuration = new SelectConfiguration
        {
            Options = new List<SelectListItem> { new("Draft", "draft", false) }
        }.ConfigurationDictionary;

        TryRead("\"draft\"", SelectFieldType.ControlName, out _, configuration).ShouldBeTrue();
        TryRead("\"signed\"", SelectFieldType.ControlName, out _, configuration).ShouldBeFalse();
    }

    [Fact]
    public void Multi_select_rejects_any_element_outside_its_options()
    {
        var configuration = new SelectConfiguration
        {
            Multiple = true,
            Options = new List<SelectListItem> { new("A", "a", false), new("B", "b", false) }
        }.ConfigurationDictionary;

        TryRead("[\"a\",\"b\"]", SelectFieldType.ControlName, out _, configuration).ShouldBeTrue();
        TryRead("[\"a\",\"z\"]", SelectFieldType.ControlName, out _, configuration).ShouldBeFalse();
    }

    /// <summary>Mirrors the schema builder: no options configured means nothing to enforce.</summary>
    [Fact]
    public void Select_without_options_accepts_any_string()
    {
        TryRead("\"anything\"", SelectFieldType.ControlName, out var result).ShouldBeTrue();
        result.ShouldBe("anything");
    }

    [Fact]
    public void Long_text_is_bounded_by_the_anti_abuse_ceiling()
    {
        var tooLong = new string('x', DocumentExtractedFieldConsts.MaxLongTextValueLength + 1);

        TryRead(JsonSerializer.Serialize(tooLong), CKEditorFieldType.ControlName, out _).ShouldBeFalse();
        TryRead(JsonSerializer.Serialize(new string('x', 5000)), CKEditorFieldType.ControlName, out _)
            .ShouldBeTrue();
    }

    /// <summary>
    /// An unknown field type cannot be validated, and storing the value unvalidated would put an untyped
    /// value into a bag every later reader trusts.
    /// </summary>
    [Fact]
    public void An_unknown_field_type_rejects_everything()
    {
        TryRead("\"value\"", "SomeFutureType", out _).ShouldBeFalse();
    }
}
