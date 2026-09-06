using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Table;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Exports;
using Dignite.Vault.Extract.FlexFields;
using Dignite.Vault.Extract.FlexFields.Tags;
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
        var raw = Json("""[{"values":{"item":"Widget","qty":3}},{"values":{"item":"Gadget","qty":1.5}}]""");

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
        var raw = Json("""[{"values":{"item":"Widget"}}]""");

        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var row = result.ShouldBeOfType<List<TableRow>>().Single();
        row.Values.ContainsKey("qty").ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_row_missing_a_required_column()
    {
        var raw = Json("""[{"values":{"qty":3}}]"""); // "item" is Required and absent

        Table.TryRead(raw, Columns, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Rejects_a_row_with_a_wrong_typed_cell()
    {
        var raw = Json("""[{"values":{"item":"Widget","qty":"not-a-number"}}]""");

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    /// <summary>Strict, like every other type: a cell key no column declares fails the whole row.</summary>
    [Fact]
    public void Rejects_a_row_carrying_a_key_no_column_declares()
    {
        var raw = Json("""[{"values":{"item":"Widget","qty":3,"extra":true}}]""");

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    /// <summary>
    /// Reject-the-whole-group, not just the bad row: a table with one incomplete row is not a complete
    /// extraction, the same philosophy Tags/Select already apply to their own elements.
    /// </summary>
    [Fact]
    public void Rejects_the_whole_table_when_any_row_is_bad()
    {
        var raw = Json("""[{"values":{"item":"Widget","qty":3}},{"values":{"qty":1}}]""");

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

    /// <summary>
    /// The bug this pins: a row's cells sit under a "values" wrapper - the kernel's own TableRow shape,
    /// which both @dignite/ng.flex-fields Table components (view and edit) require - not at the row's own
    /// top level. A row shaped like the pre-fix output would have every cell silently ignored by
    /// ff-table-view (empty cells rendered, no error) rather than rejected here - this is the loud failure
    /// that should happen instead.
    /// </summary>
    [Fact]
    public void Rejects_a_row_missing_the_values_wrapper()
    {
        var raw = Json("""[{"item":"Widget","qty":3}]"""); // pre-fix flat shape, no longer accepted

        Table.TryRead(raw, Columns, out _).ShouldBeFalse();
    }

    [Fact]
    public void Writes_rows_back_wrapped_under_values_keyed_by_column_name()
    {
        var raw = Json("""[{"values":{"item":"Widget","qty":3}}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var written = Table.WriteJson(result!, Columns);

        written.ShouldNotBeNull();
        written!.Value.ValueKind.ShouldBe(JsonValueKind.Array);
        var row = written.Value.EnumerateArray().Single();
        var values = row.GetProperty("values");
        values.GetProperty("item").GetString().ShouldBe("Widget");
        values.GetProperty("qty").GetDecimal().ShouldBe(3m);
    }

    /// <summary>An absent cell round-trips as an absent key, not a null one.</summary>
    [Fact]
    public void Writes_an_absent_cell_as_an_absent_key()
    {
        var raw = Json("""[{"values":{"item":"Widget"}}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var row = Table.WriteJson(result!, Columns)!.Value.EnumerateArray().Single();

        row.GetProperty("values").TryGetProperty("qty", out _).ShouldBeFalse();
    }

    [Fact]
    public void Builds_an_array_of_row_objects_schema_wrapping_each_columns_own_schema_under_values()
    {
        var schema = Table.BuildExtractionSchema(Columns);

        schema["type"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldContain("array");
        var itemSchema = schema["items"]!.AsObject();
        itemSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(new[] { "values" });
        var valuesSchema = itemSchema["properties"]!.AsObject()["values"]!.AsObject();
        var properties = valuesSchema["properties"]!.AsObject();
        properties.ContainsKey("item").ShouldBeTrue();
        properties.ContainsKey("qty").ShouldBeTrue();
        valuesSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(new[] { "item" });
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
        var raw = Json("""[{"values":{"item":"Widget","qty":3}}]""");
        Table.TryRead(raw, Columns, out var result).ShouldBeTrue();

        var cell = ExportCellRenderer.RenderCell(result, TableFieldType.ControlName, Columns, TestFieldTypeRegistry.Default);

        cell.ShouldNotBeNull();
        // The old bug's tell: a CLR type name, or elements joined with the multi-value separator.
        cell.ShouldNotContain("TableRow");
        cell.ShouldNotContain(ExportCellRenderer.MultiValueSeparator);

        var parsed = JsonSerializer.Deserialize<JsonElement>(cell!);
        parsed.ValueKind.ShouldBe(JsonValueKind.Array);
        var values = parsed[0].GetProperty("values");
        values.GetProperty("item").GetString().ShouldBe("Widget");
        values.GetProperty("qty").GetDecimal().ShouldBe(3m);
    }

    /// <summary>
    /// #625 follow-up: every other column type tested above contributes exactly one string per cell.
    /// <c>Tags</c> (and multi-<c>Select</c>) is the first column type that contributes a
    /// <b>variable-length run</b> of strings per cell - this exercises the full round trip (<c>TryRead</c>
    /// -&gt; <c>WriteJson</c> -&gt; <c>CanonicalizeForFingerprint</c>) for that shape.
    /// </summary>
    [Fact]
    public void Reads_writes_and_canonicalizes_a_row_whose_cell_is_a_multi_valued_column()
    {
        var columnsWithTags = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "labels", DisplayName = "Labels", FieldTypeName = TagsFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        var raw = Json("""[{"values":{"item":"Widget","labels":["urgent","legal"]}},{"values":{"item":"Gadget","labels":["low"]}}]""");

        Table.TryRead(raw, columnsWithTags, out var result).ShouldBeTrue();
        var rows = result.ShouldBeOfType<List<TableRow>>();
        rows.Count.ShouldBe(2);
        rows[0].Values["labels"].ShouldBeOfType<List<string>>().ShouldBe(new[] { "urgent", "legal" });
        rows[1].Values["labels"].ShouldBeOfType<List<string>>().ShouldBe(new[] { "low" });

        var written = Table.WriteJson(result!, columnsWithTags);
        written.ShouldNotBeNull();
        var writtenRows = written!.Value.EnumerateArray().Select(r => r.GetProperty("values")).ToList();
        writtenRows[0].GetProperty("labels").EnumerateArray().Select(e => e.GetString()).ShouldBe(new[] { "urgent", "legal" });
        writtenRows[1].GetProperty("labels").EnumerateArray().Select(e => e.GetString()).ShouldBe(new[] { "low" });

        // item (1 string/cell) then labels (N strings/cell), per row, in column order.
        Table.CanonicalizeForFingerprint(rows, columnsWithTags)
            .ShouldBe(new[] { "widget", "urgent", "legal", "gadget", "low" });
    }

    /// <summary>
    /// KNOWN LIMITATION, not a regression from this pass — reported per the #625 code-review brief rather
    /// than silently fixed, because a fix changes what an existing Table-based unique key hashes to (a
    /// fingerprint-contract change, CLAUDE.md "decide whether an Issue is needed" territory).
    /// <para>
    /// <see cref="TableFieldTypeExtension.CanonicalizeForFingerprint"/> flattens every cell's contribution
    /// into one list with <c>AddRange</c> and no boundary marker between cells or rows. That is safe for
    /// every column type tested above, each of which contributes exactly one string per cell — but once a
    /// column is multi-valued (<c>Tags</c> / multi-<c>Select</c>), a cell can contribute a
    /// <b>variable-length</b> run of strings, and the row/cell boundary that run's length used to mark is
    /// lost the moment it is flattened. Two structurally different tables would then flatten to the
    /// identical canonical sequence, which <c>FlexFieldFingerprintCalculator</c> (#411) would hash
    /// identically - a false "these are the same document" duplicate-detection collision, IF a Table field
    /// could ever be marked <c>IsUniqueKey</c> in the first place.
    /// </para>
    /// <para>
    /// #626: it no longer can. <c>Table</c>'s <c>IndexValueType</c> is <c>null</c>, so
    /// <c>FieldDefinitionAppService.CheckUniqueKey</c> now rejects <c>IsUniqueKey = true</c> for any Table
    /// field before it can be persisted, the same way <c>CheckSearchable</c> already rejects
    /// <c>IsSearchable</c> for it - this collision is unreachable via the public API. The method under test
    /// here, <see cref="TableFieldTypeExtension.CanonicalizeForFingerprint"/>, still exhibits the flattening
    /// behavior this test pins, since fingerprint canonicalization has no idea a field is (or isn't) a
    /// unique key; the test remains as a direct unit pin on that method, not as a live production risk.
    /// </para>
    /// <para>
    /// This test pins the CURRENT behavior (the two tables below canonicalize identically) so a future fix
    /// changes a red assertion, not a silent drift. Every other column-type combination pinned above is
    /// unaffected: the ambiguity only exists when a multi-valued column shares a row with a scalar one.
    /// </para>
    /// </summary>
    [Fact]
    public void KNOWN_LIMITATION_Multi_valued_columns_can_make_two_different_tables_canonicalize_identically()
    {
        var columnsWithTags = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "labels", DisplayName = "Labels", FieldTypeName = TagsFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        // Table A: row1 has a 2-tag cell then item "x"; row2 has a 1-tag cell then item "y".
        var tableA = new List<TableRow>
        {
            new() { Values = new FlexFieldDictionary { ["item"] = "a", ["labels"] = new List<string> { "b", "x" } } },
            new() { Values = new FlexFieldDictionary { ["item"] = "c", ["labels"] = new List<string> { "y" } } }
        };

        // Table B: a DIFFERENT row/cell split - row1 has a 1-tag cell then item "b"; row2 has a 2-tag cell
        // then item "y". Not the same data by any reasonable reading, and not equal as List<TableRow>.
        var tableB = new List<TableRow>
        {
            new() { Values = new FlexFieldDictionary { ["item"] = "a", ["labels"] = new List<string> { "b" } } },
            new() { Values = new FlexFieldDictionary { ["item"] = "x", ["labels"] = new List<string> { "c", "y" } } }
        };

        var canonicalA = Table.CanonicalizeForFingerprint(tableA, columnsWithTags);
        var canonicalB = Table.CanonicalizeForFingerprint(tableB, columnsWithTags);

        canonicalA.ShouldBe(canonicalB);
        canonicalA.ShouldBe(new[] { "a", "b", "x", "c", "y" });
    }
}
