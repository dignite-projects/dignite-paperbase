using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Table;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Exports;
using Dignite.Vault.Extract.FlexFields;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// <see cref="TableFieldTypeExtension"/> (#625) — the composite grid type, whose per-column recursion
/// (through <see cref="TableFieldTypeExtension.Registry"/>) is exercised end to end here rather than
/// through a hand-built value bypassing real dispatch. Uses <see cref="TestFieldTypeRegistry"/>, the same
/// registry every other type's dispatcher tests use, resolved to the <c>Table</c> extension it wires up.
/// <para>
/// Table's own per-type behavior (composite recursion, extra/missing-cell rejection) gets its dedicated
/// coverage here; its wiring into the five shared dispatchers is additionally pinned by a case each in
/// <c>FlexFieldValueReader_Tests</c> / <c>FlexFieldValueSchemaBuilder_Tests</c> /
/// <c>ExportCellRenderer_Tests</c> / <c>FlexFieldFingerprintCalculator_Tests</c>, matching how every other
/// field type is covered there.
/// </para>
/// </summary>
public class TableFieldTypeExtension_Tests
{
    private static readonly IVaultExtractFieldTypeExtension Table =
        TestFieldTypeRegistry.Default.Get(TableFieldType.ControlName);

    private static readonly FieldConfigurationDictionary Columns = new TableConfiguration
    {
        Columns = new List<InlineFieldDefinition>
        {
            new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
            new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
        }
    }.ConfigurationDictionary;

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    [Fact]
    public void Is_never_reported_as_multi_valued()
    {
        // #625 design decision: a List<TableRow> value is one composite scalar to the shared dispatchers,
        // never "repeated scalars of the same type" the way Tags/multi-Select are.
        Table.IsMultiValue(Columns).ShouldBeFalse();
    }

    [Fact]
    public void Reads_multiple_rows_delegating_each_cell_to_its_own_column_type()
    {
        var raw = Json("""[{"item":"Widget","qty":3},{"item":"Gadget","qty":1.5}]""");

        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var rows = result.ShouldBeOfType<List<TableRow>>();
        rows.Count.ShouldBe(2);
        rows[0].Values["item"].ShouldBe("Widget");
        rows[0].Values["qty"].ShouldBe(3m);
        rows[1].Values["item"].ShouldBe("Gadget");
        rows[1].Values["qty"].ShouldBe(1.5m);
    }

    /// <summary>An absent, non-Required cell is simply not stored - not an empty string, not a rejection.</summary>
    [Fact]
    public void An_absent_optional_cell_is_not_stored()
    {
        var raw = Json("""[{"item":"Widget"}]""");

        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var row = result.ShouldBeOfType<List<TableRow>>().Single();
        row.Values.ContainsKey("qty").ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_row_missing_a_required_column()
    {
        var raw = Json("""[{"qty":3}]"""); // "item" is Required and absent

        Table.TryRead(raw, Columns, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Rejects_a_row_with_a_wrong_typed_cell()
    {
        var raw = Json("""[{"item":"Widget","qty":"not-a-number"}]""");

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    /// <summary>Strict, like every other type: a cell key no column declares fails the whole row.</summary>
    [Fact]
    public void Rejects_a_row_carrying_a_key_no_column_declares()
    {
        var raw = Json("""[{"item":"Widget","qty":3,"extra":true}]""");

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Reject-the-whole-group, not just the bad row: a table with one incomplete row is not a complete
    /// extraction, the same philosophy Tags/Select already apply to their own elements.
    /// </summary>
    [Fact]
    public void Rejects_the_whole_table_when_any_row_is_bad()
    {
        var raw = Json("""[{"item":"Widget","qty":3},{"qty":1}]""");

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_non_array_value()
    {
        Table.TryRead(Json("""{"item":"Widget"}"""), Columns, out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_row_that_is_not_an_object()
    {
        Table.TryRead(Json("""["Widget"]"""), Columns, out _).ShouldBeFalse();
    }

    [Fact]
    public void Writes_rows_back_as_a_flat_json_array_keyed_by_column_name()
    {
        var raw = Json("""[{"item":"Widget","qty":3}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var written = Table.WriteJson(result!, Columns);

        written.ShouldNotBeNull();
        written!.Value.ValueKind.ShouldBe(JsonValueKind.Array);
        var row = written.Value.EnumerateArray().Single();
        row.GetProperty("item").GetString().ShouldBe("Widget");
        row.GetProperty("qty").GetDecimal().ShouldBe(3m);
    }

    /// <summary>An absent cell round-trips as an absent key, not a null one.</summary>
    [Fact]
    public void Writes_an_absent_cell_as_an_absent_key()
    {
        var raw = Json("""[{"item":"Widget"}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var row = Table.WriteJson(result!, Columns)!.Value.EnumerateArray().Single();

        row.TryGetProperty("qty", out _).ShouldBeFalse();
    }

    [Fact]
    public void Builds_an_array_of_row_objects_schema_with_each_columns_own_schema()
    {
        var schema = Table.BuildExtractionSchema(Columns);

        schema["type"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldContain("array");
        var itemSchema = schema["items"]!.AsObject();
        var properties = itemSchema["properties"]!.AsObject();
        properties.ContainsKey("item").ShouldBeTrue();
        properties.ContainsKey("qty").ShouldBeTrue();
        itemSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(new[] { "item" });
    }

    [Fact]
    public void Canonicalizes_each_row_cell_by_cell_in_column_order()
    {
        var rows = new List<TableRow>
        {
            new() { Values = new FlexFieldDictionary { ["item"] = "Widget", ["qty"] = 3m } },
            new() { Values = new FlexFieldDictionary { ["item"] = "Gadget", ["qty"] = 1.5m } }
        };

        Table.CanonicalizeForFingerprint(rows, Columns)
            .ShouldBe(new[] { "widget", "3", "gadget", "1.5" });
    }

    /// <summary>
    /// One missing cell makes the WHOLE field's contribution empty, never a shorter one - the same rule
    /// <c>FieldTypeExtensionHelpers.CanonicalizeListForFingerprint</c> enforces for Tags/multi-Select.
    /// </summary>
    [Fact]
    public void A_missing_cell_makes_the_whole_contribution_empty()
    {
        var rows = new List<TableRow>
        {
            new() { Values = new FlexFieldDictionary { ["item"] = "Widget" } } // "qty" missing
        };

        Table.CanonicalizeForFingerprint(rows, Columns).ShouldBeEmpty();
    }

    [Fact]
    public void A_blank_cell_value_makes_the_whole_contribution_empty()
    {
        var rows = new List<TableRow>
        {
            new() { Values = new FlexFieldDictionary { ["item"] = "   ", ["qty"] = 3m } }
        };

        Table.CanonicalizeForFingerprint(rows, Columns).ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_table_contributes_nothing()
    {
        Table.CanonicalizeForFingerprint(new List<TableRow>(), Columns).ShouldBeEmpty();
    }

    /// <summary>
    /// The #625 regression this whole feature would otherwise expose silently: without the
    /// <c>ExportCellRenderer</c> fix, a Table value's own <c>IEnumerable</c>-ness would be caught by the
    /// old shape-first list detection and rendered as <c>Convert.ToString(row)</c> per row (a CLR type
    /// name) instead of ever reaching <see cref="TableFieldTypeExtension.RenderForExport"/>. Goes through
    /// the real <see cref="ExportCellRenderer.RenderCell"/> -&gt; registry path, not a hand-built value.
    /// </summary>
    [Fact]
    public void Export_cell_reaches_RenderForExport_not_the_generic_list_branch()
    {
        var raw = Json("""[{"item":"Widget","qty":3}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var cell = ExportCellRenderer.RenderCell(result, TableFieldType.ControlName, Columns, TestFieldTypeRegistry.Default);

        cell.ShouldNotBeNull();
        // The old bug's tell: a CLR type name, or elements joined with the multi-value separator.
        cell.ShouldNotContain("TableRow");
        cell.ShouldNotContain(ExportCellRenderer.MultiValueSeparator);

        var parsed = JsonSerializer.Deserialize<JsonElement>(cell!);
        parsed.ValueKind.ShouldBe(JsonValueKind.Array);
        parsed[0].GetProperty("item").GetString().ShouldBe("Widget");
        parsed[0].GetProperty("qty").GetDecimal().ShouldBe(3m);
    }
}
