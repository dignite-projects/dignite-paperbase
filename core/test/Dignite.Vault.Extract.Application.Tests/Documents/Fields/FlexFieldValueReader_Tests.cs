using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Table;
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
            Json(json), fieldTypeName, configuration ?? new FieldConfigurationDictionary(),
            TestFieldTypeRegistry.Default, out result);

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

    /// <summary>
    /// A DateTime field stores wall-clock time with no offset, so an offset-bearing or Z-suffixed string is
    /// rejected rather than silently reinterpreted in some local zone. This assertion moved here from
    /// <c>FieldExtractionWorkflow_Tests</c> when validation collapsed into the reader — the guarantee is the
    /// same, it just has one owner now.
    /// </summary>
    [Theory]
    [InlineData("\"2024-01-01T10:00:00+08:00\"")]
    [InlineData("\"2024-01-01T10:00:00Z\"")]
    public void Datetime_rejects_an_offset_bearing_string(string json)
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        TryRead(json, "DateTime", out _, configuration).ShouldBeFalse();
    }

    [Fact]
    public void Datetime_mode_reads_the_full_moment()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        TryRead("\"2026-03-14T10:30:00\"", "DateTime", out var result, configuration).ShouldBeTrue();
        result.ShouldBe(new DateTime(2026, 3, 14, 10, 30, 0));
    }

    /// <summary>
    /// The shapes a browser actually produces for a DateTime-mode field, both of which used to be rejected —
    /// which meant such a field could not be saved from the operator UI at all. Seeding
    /// <c>&lt;input type="datetime-local"&gt;</c> writes the space-separated form, and editing it hands back a
    /// <c>T</c>-separated one with the seconds dropped when they are zero. Both denote the same wall-clock
    /// moment as the canonical form and are normalized to it, so nothing downstream — the bag, the egress
    /// rendering, the #411 fingerprint — can tell which shape arrived.
    /// </summary>
    [Theory]
    [InlineData("\"2026-03-14T10:30\"")]
    [InlineData("\"2026-03-14 10:30:00\"")]
    [InlineData("\"2026-03-14 10:30\"")]
    public void Datetime_mode_normalizes_the_shapes_a_browser_produces(string json)
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        TryRead(json, "DateTime", out var result, configuration).ShouldBeTrue();
        result.ShouldBe(new DateTime(2026, 3, 14, 10, 30, 0));
    }

    /// <summary>
    /// Widening the accepted input shapes must not widen what a <i>Date</i>-mode field takes: its stored
    /// value is midnight so an equality filter stays an equality filter, and letting a time component in
    /// through the back door would break that quietly.
    /// </summary>
    [Theory]
    [InlineData("\"2026-03-14 10:30:00\"")]
    [InlineData("\"2026-03-14T10:30\"")]
    public void Date_mode_still_rejects_a_time_component(string json)
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;

        TryRead(json, "DateTime", out _, configuration).ShouldBeFalse();
    }

    /// <summary>
    /// Month mode is a date whose day carries no information, so it is stored as the first of the month at
    /// midnight — an ordinary DateTime that sorts, ranges and indexes like any other, with the day pinned
    /// rather than left to whatever the parser happened to fill in.
    /// </summary>
    [Fact]
    public void Month_mode_stores_the_first_of_the_month_at_midnight()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Month }.ConfigurationDictionary;

        TryRead("\"2026-03\"", "DateTime", out var result, configuration).ShouldBeTrue();
        result.ShouldBe(new DateTime(2026, 3, 1, 0, 0, 0));
    }

    /// <summary>
    /// The day is pinned explicitly rather than trusted to the parser. Guards the property that actually
    /// matters — that the stored day never depends on when the value was saved — independently of whatever
    /// <c>DateOnly.TryParseExact</c> does with a format that has no day component.
    /// </summary>
    [Fact]
    public void Month_mode_stores_the_same_day_whatever_today_is()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Month }.ConfigurationDictionary;

        // February, so a parser that filled the day from "today" would land on an invalid date for most
        // of the month and on the 29th at best.
        TryRead("\"2026-02\"", "DateTime", out var result, configuration).ShouldBeTrue();
        ((DateTime)result!).Day.ShouldBe(1);
    }

    /// <summary>
    /// A month field must not accept a full date: the day would be silently discarded, so a document
    /// stating the 14th would be stored as the 1st with nothing recording that a day was ever given.
    /// </summary>
    [Theory]
    [InlineData("\"2026-03-14\"")]
    [InlineData("\"2026-03-14T10:30:00\"")]
    public void Month_mode_rejects_a_full_date(string json)
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Month }.ConfigurationDictionary;

        TryRead(json, "DateTime", out _, configuration).ShouldBeFalse();
    }

    /// <summary>Conversely, a Date-mode field must not accept a bare month.</summary>
    [Fact]
    public void Date_mode_rejects_a_bare_month()
    {
        var configuration = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;

        TryRead("\"2026-03\"", "DateTime", out _, configuration).ShouldBeFalse();
    }

    /// <summary>
    /// Characterizes the framework behaviour the reader's Month branch sits on: parsing a day-less format
    /// already defaults the day to the 1st, independently of today's date. The reader pins the day anyway
    /// — this test is what says the pinning is belt-and-braces rather than load-bearing, and it reddens if
    /// a future runtime changes the default instead of the Month tests failing for an unexplained reason.
    /// </summary>
    [Fact]
    public void DateOnly_defaults_a_missing_day_to_the_first()
    {
        DateOnly.TryParseExact(
                "2026-02", FieldValueFormats.Month, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            .ShouldBeTrue();

        date.ShouldBe(new DateOnly(2026, 2, 1));
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
    /// A bare string is not "one tag": a multi-valued field's shape is the array, and accepting a scalar
    /// would put a <c>string</c> where every later reader expects a <c>List&lt;string&gt;</c>. The extraction
    /// workflow no longer checks this itself — it hands the raw JsonElement through — so this is the only
    /// gate between a model that ignored the array schema and the value bag.
    /// </summary>
    [Fact]
    public void Tags_rejects_a_scalar()
    {
        TryRead("\"urgent\"", TagsFieldType.ControlName, out _).ShouldBeFalse();
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

    /// <summary>
    /// A Select with no options accepts nothing, matching the kernel's own
    /// <c>SelectFieldType.Validate</c> (<c>value.Except(options).Any()</c> rejects everything when the
    /// option list is empty). An earlier reading here accepted anything, which would have let extraction
    /// store values that kernel validation rejects the moment the cutover wires it.
    /// </summary>
    [Fact]
    public void Select_without_options_accepts_nothing()
    {
        TryRead("\"anything\"", SelectFieldType.ControlName, out _).ShouldBeFalse();
    }

    /// <summary>
    /// The field type's configured bounds are enforced here too, for the same reason: the kernel's
    /// DateTimeFieldType.Validate enforces them, and a value accepted by extraction but rejected by
    /// validation is a divergence that only shows up at the cutover.
    /// </summary>
    [Fact]
    public void Datetime_outside_its_configured_range_is_rejected()
    {
        var configuration = new DateTimeConfiguration
        {
            InputMode = DateTimeInputMode.Date,
            Min = new DateTime(2026, 1, 1),
            Max = new DateTime(2026, 12, 31)
        }.ConfigurationDictionary;

        TryRead("\"2026-06-15\"", "DateTime", out _, configuration).ShouldBeTrue();
        TryRead("\"2025-12-31\"", "DateTime", out _, configuration).ShouldBeFalse();
        TryRead("\"2027-01-01\"", "DateTime", out _, configuration).ShouldBeFalse();
    }

    /// <summary>
    /// #625: pins that the composite Table type is actually wired into the shared dispatcher, not just
    /// correct in isolation - each cell is delegated to its own column's own registered extension.
    /// </summary>
    [Fact]
    public void Table_reads_rows_delegating_each_cell_to_its_own_column_type()
    {
        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        TryRead("""[{"item":"Widget","qty":3}]""", TableFieldType.ControlName, out var result, configuration)
            .ShouldBeTrue();

        var row = result.ShouldBeOfType<List<TableRow>>().Single();
        row.Values["item"].ShouldBe("Widget");
        row.Values["qty"].ShouldBe(3m);
    }

    [Fact]
    public void Table_rejects_a_row_missing_a_required_column()
    {
        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true }
            }
        }.ConfigurationDictionary;

        TryRead("[{}]", TableFieldType.ControlName, out _, configuration).ShouldBeFalse();
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
