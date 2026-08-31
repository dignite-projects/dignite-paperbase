using System;
using System.Collections.Generic;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Select;
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
        => ExportCellRenderer.RenderCell(value, fieldTypeName, config ?? NoConfig);

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
