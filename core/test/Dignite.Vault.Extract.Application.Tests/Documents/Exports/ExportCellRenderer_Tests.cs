using System;
using System.Collections.Generic;
using System.Text.Json;
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

namespace Dignite.Vault.Extract.Documents.Exports;

/// <summary>
/// Unit tests for <see cref="ExportCellRenderer"/>. Internal and visible through InternalsVisibleTo; a pure
/// function, so no DB and no mocks.
/// <para>
/// Under v2 these existed because the EF export tests could not pin the multi-value sort: SQLite returned
/// child rows already ascending by <c>Order</c>, so the integration test stayed green with the sort deleted.
/// v3 removes that hazard at the root — order is the bag's list order, not a column the database happens to
/// sort by — but the rendering rules still need pinning somewhere, and a pure function is the place.
/// </para>
/// </summary>
public class ExportCellRenderer_Tests
{
    private static readonly FieldConfigurationDictionary NoConfig = new();

    private static string? Render(object? value, string fieldTypeName, FieldConfigurationDictionary? config = null)
        => ExportCellRenderer.RenderCell(value, fieldTypeName, config ?? NoConfig, TestFieldTypeRegistry.Default);

    [Fact]
    public void Renders_a_multi_value_field_in_bag_order()
    {
        var value = new List<string> { "urgent", "legal", "2026" };

        Render(value, TagsFieldType.ControlName).ShouldBe("urgent; legal; 2026");
    }

    [Fact]
    public void Renders_a_single_value_field_as_the_bare_value_with_no_separator()
    {
        Render("sole", "Text").ShouldBe("sole");
    }

    [Fact]
    public void Renders_a_missing_value_as_an_empty_cell()
    {
        Render(null, "Text").ShouldBeNull();
    }

    [Fact]
    public void Renders_an_empty_multi_value_field_as_an_empty_cell()
    {
        Render(new List<string>(), TagsFieldType.ControlName).ShouldBeNull();
    }

    /// <summary>An empty element contributes nothing and must not leave a stray separator behind.</summary>
    [Fact]
    public void Skips_empty_elements_of_a_multi_value_field()
    {
        Render(new List<string> { "kept", "" }, TagsFieldType.ControlName).ShouldBe("kept");
    }

    [Fact]
    public void Renders_each_field_type_in_its_canonical_shape()
    {
        Render("hello", "Text").ShouldBe("hello");
        Render("body", CKEditorFieldType.ControlName).ShouldBe("body");

        // Minimal shape: no six trailing zeros from decimal(38,6).
        Render(1000m, "Number").ShouldBe("1000");
        Render(10.50m, "Number").ShouldBe("10.5");

        Render(true, "Boolean").ShouldBe("true");
        Render(false, "Boolean").ShouldBe("false");
    }

    /// <summary>
    /// Date and DateTime share one field type in v3, so the cell shape follows InputMode. A Date-mode field
    /// must still export the bare date it exported under v2, not the midnight instant the bag stores.
    /// </summary>
    [Fact]
    public void Date_and_datetime_modes_render_differently()
    {
        var date = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;
        var dateTime = new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary;

        Render(new DateTime(2026, 3, 4), "DateTime", date).ShouldBe("2026-03-04");
        Render(new DateTime(2026, 3, 4, 5, 6, 7), "DateTime", dateTime).ShouldBe("2026-03-04T05:06:07");
    }

    /// <summary>
    /// A bag reloaded from the database holds JsonElements rather than CLR values, and an export runs over
    /// exactly those. Both shapes must render identically or the file differs depending on whether the
    /// document happened to be freshly written.
    /// </summary>
    [Fact]
    public void Renders_json_round_tripped_values_identically()
    {
        Render(Json("\"hello\""), "Text").ShouldBe("hello");
        Render(Json("10.50"), "Number").ShouldBe("10.5");
        Render(Json("true"), "Boolean").ShouldBe("true");
        Render(Json("[\"a\",\"b\"]"), TagsFieldType.ControlName).ShouldBe("a; b");

        var date = new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary;
        Render(Json("\"2026-03-04T00:00:00\""), "DateTime", date).ShouldBe("2026-03-04");
    }

    [Fact]
    public void Renders_a_multi_select_as_a_joined_list()
    {
        var config = new SelectConfiguration { Multiple = true }.ConfigurationDictionary;

        Render(new List<string> { "draft", "signed" }, SelectFieldType.ControlName, config)
            .ShouldBe("draft; signed");
    }

    /// <summary>
    /// The #625 regression this feature would otherwise expose silently: <c>List&lt;TableRow&gt;</c> IS an
    /// <see cref="System.Collections.IEnumerable"/> while being one composite scalar
    /// (<c>IsMultiValue = false</c>). Before the fix, the shape-first list detection here could not tell
    /// that apart from a genuine multi-valued list, and would have rendered each row via
    /// <c>Convert.ToString(row)</c> - a CLR type name - instead of ever reaching
    /// <c>TableFieldTypeExtension.RenderForExport</c>. Built through the real <c>TryRead</c> -&gt;
    /// <c>RenderCell</c> path, not a hand-built value that would bypass real dispatch.
    /// </summary>
    [Fact]
    public void Table_export_cell_reaches_RenderForExport_not_the_generic_list_branch()
    {
        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        TestFieldTypeRegistry.Default.Get(TableFieldType.ControlName)
            .TryRead(Json("""[{"item":"Widget","qty":3}]"""), configuration, out var value)
            .ShouldBeTrue();

        var cell = Render(value, TableFieldType.ControlName, configuration);

        cell.ShouldNotBeNull();
        cell.ShouldNotContain("TableRow");
        cell.ShouldNotContain(ExportCellRenderer.MultiValueSeparator);

        var parsed = Json(cell!);
        parsed.ValueKind.ShouldBe(JsonValueKind.Array);
        parsed[0].GetProperty("item").GetString().ShouldBe("Widget");
        parsed[0].GetProperty("qty").GetDecimal().ShouldBe(3m);
    }

    /// <summary>
    /// #625 follow-up: an empty (non-null, zero-row) Table value must render as an empty cell, exactly like
    /// an empty Tags/multi-Select list does above - never the literal text <c>"[]"</c>, which
    /// <c>TableFieldTypeExtension.RenderForExport</c> would otherwise produce by unconditionally delegating
    /// to <c>WriteJson</c>. Goes through the real <c>TryRead</c> -&gt; <c>RenderCell</c> path, mirroring
    /// <see cref="Table_export_cell_reaches_RenderForExport_not_the_generic_list_branch"/> above.
    /// </summary>
    [Fact]
    public void Empty_table_export_cell_is_null_not_the_literal_text_empty_brackets()
    {
        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        TestFieldTypeRegistry.Default.Get(TableFieldType.ControlName)
            .TryRead(Json("[]"), configuration, out var value)
            .ShouldBeTrue();

        Render(value, TableFieldType.ControlName, configuration).ShouldBeNull();
    }

    /// <summary>
    /// A field type this renderer does not know must break loudly. Carried over from v2's switch: a silently
    /// wrong cell in a file handed to an accountant is worse than an error.
    /// </summary>
    [Fact]
    public void Loud_fails_on_a_field_type_it_does_not_know()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Render("x", "SomeFutureType"));
    }

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);
}
