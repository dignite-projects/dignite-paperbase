using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Table;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields.Tags;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// <see cref="FlexFieldFingerprintCalculator"/> — duplicate detection (#411) over the v3 value bag.
/// <para>
/// The fingerprint is a stored hash compared by string equality, so almost every failure mode here is
/// silent: two documents that should match stop matching, or two that should not start matching. None of
/// it throws.
/// </para>
/// </summary>
public class FlexFieldFingerprintCalculator_Tests
{
    private sealed class Bag : IHasFlexFields
    {
        public FlexFieldDictionary FlexFields { get; } = new();
    }

    private static readonly Guid InvoiceNoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AmountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid IssuedOnId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Field UniqueKey(Guid id, string name, string fieldTypeName)
        => new(id, null, Guid.NewGuid(), name, name, fieldTypeName, isUniqueKey: true);

    private static Field UniqueKey(Guid id, string name, string fieldTypeName, FieldConfigurationDictionary configuration)
        => new(id, null, Guid.NewGuid(), name, name, fieldTypeName, configuration: configuration, isUniqueKey: true);

    private static Field Ordinary(string name, string fieldTypeName)
        => new(Guid.NewGuid(), null, Guid.NewGuid(), name, name, fieldTypeName);

    private static readonly List<Field> Schema = new()
    {
        UniqueKey(InvoiceNoId, "invoice_no", "Text"),
        UniqueKey(AmountId, "amount", "Number"),
        UniqueKey(IssuedOnId, "issued_on", "DateTime"),
        Ordinary("notes", "Text")
    };

    private static IHasFlexFields Doc(params (string Name, object? Value)[] values)
    {
        var bag = new Bag();
        foreach (var (name, value) in values)
        {
            if (value != null)
            {
                bag.SetField(name, value);
            }
        }
        return bag;
    }

    private static IHasFlexFields Complete(
        string invoiceNo = "INV-001",
        decimal amount = 100m,
        DateTime? issuedOn = null,
        string? notes = null)
        => Doc(
            ("invoice_no", invoiceNo),
            ("amount", amount),
            ("issued_on", issuedOn ?? new DateTime(2026, 3, 14)),
            ("notes", notes));

    [Fact]
    public void Same_values_hash_the_same()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(), Schema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(Complete(), Schema, TestFieldTypeRegistry.Default));
    }

    [Fact]
    public void Different_values_hash_differently()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(invoiceNo: "INV-001"), Schema, TestFieldTypeRegistry.Default)
            .ShouldNotBe(FlexFieldFingerprintCalculator.Compute(Complete(invoiceNo: "INV-002"), Schema, TestFieldTypeRegistry.Default));
    }

    /// <summary>Non-key fields must not participate, or an edited note would break duplicate detection.</summary>
    [Fact]
    public void Non_key_fields_do_not_participate()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(notes: "first scan"), Schema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(Complete(notes: "second scan"), Schema, TestFieldTypeRegistry.Default));
    }

    /// <summary>Cosmetic differences between two scans of the same document must still match.</summary>
    [Theory]
    [InlineData("INV-001", "inv-001")]
    [InlineData("INV 001", "INV  001")]
    [InlineData("INV-001", "  INV-001  ")]
    public void Text_is_normalized(string a, string b)
    {
        FlexFieldFingerprintCalculator.Compute(Complete(invoiceNo: a), Schema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(Complete(invoiceNo: b), Schema, TestFieldTypeRegistry.Default));
    }

    [Fact]
    public void Numbers_ignore_trailing_zeros()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(amount: 100m), Schema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(Complete(amount: 100.00m), Schema, TestFieldTypeRegistry.Default));
    }

    /// <summary>
    /// But only trailing zeros. Full precision is deliberate: two amounts differing beyond six decimals
    /// are different amounts, whatever the export happens to round them to.
    /// </summary>
    [Fact]
    public void Numbers_keep_full_precision()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(amount: 100.0000001m), Schema, TestFieldTypeRegistry.Default)
            .ShouldNotBe(FlexFieldFingerprintCalculator.Compute(Complete(amount: 100.0000002m), Schema, TestFieldTypeRegistry.Default));
    }

    [Fact]
    public void No_unique_key_fields_means_no_fingerprint()
    {
        var schema = new List<Field> { Ordinary("invoice_no", "Text") };

        FlexFieldFingerprintCalculator.Compute(Complete(), schema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    /// <summary>
    /// A partial key would collide unrelated documents, so it is deliberately not fingerprinted — fewer
    /// false positives, at the cost of missing a duplicate whose key field failed to extract.
    /// </summary>
    [Fact]
    public void A_missing_key_value_makes_the_key_partial()
    {
        var doc = Doc(("invoice_no", "INV-001"), ("amount", 100m));

        FlexFieldFingerprintCalculator.Compute(doc, Schema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    [Fact]
    public void A_blank_key_value_makes_the_key_partial()
    {
        FlexFieldFingerprintCalculator.Compute(Complete(invoiceNo: "   "), Schema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    /// <summary>
    /// The bag holds CLR values in memory and JsonElements after a reload. Both must hash identically, or
    /// a document would fingerprint differently before and after being reloaded.
    /// </summary>
    [Fact]
    public void Json_round_tripped_values_hash_identically()
    {
        var inMemory = Complete();

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["invoice_no"] = "INV-001",
            ["amount"] = 100m,
            ["issued_on"] = new DateTime(2026, 3, 14)
        });
        var reloaded = new Bag();
        foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!)
        {
            reloaded.SetField(pair.Key, pair.Value);
        }

        FlexFieldFingerprintCalculator.Compute(reloaded, Schema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(inMemory, Schema, TestFieldTypeRegistry.Default));
    }

    // --- multi-valued keys ---

    private static readonly List<Field> TagSchema = new()
    {
        UniqueKey(InvoiceNoId, "parties", TagsFieldType.ControlName)
    };

    [Fact]
    public void Multi_valued_key_order_changes_the_hash()
    {
        var ab = Doc(("parties", new List<string> { "Acme", "Globex" }));
        var ba = Doc(("parties", new List<string> { "Globex", "Acme" }));

        // Order is part of the value, not incidental: the bag preserves it, so two documents whose party
        // lists differ only in order are genuinely different extractions.
        FlexFieldFingerprintCalculator.Compute(ab, TagSchema, TestFieldTypeRegistry.Default)
            .ShouldNotBe(FlexFieldFingerprintCalculator.Compute(ba, TagSchema, TestFieldTypeRegistry.Default));
    }

    /// <summary>
    /// A blank element makes the whole key partial rather than shortening it. Dropping it would let
    /// ["Acme", ""] and ["Acme"] — different extractions — hash the same.
    /// </summary>
    [Fact]
    public void A_blank_element_makes_a_multi_valued_key_partial()
    {
        var doc = Doc(("parties", new List<string> { "Acme", "  " }));

        FlexFieldFingerprintCalculator.Compute(doc, TagSchema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    [Fact]
    public void An_empty_multi_valued_key_is_partial()
    {
        FlexFieldFingerprintCalculator.Compute(Doc(("parties", new List<string>())), TagSchema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    /// <summary>
    /// An unknown field type must not produce a fingerprint from an arbitrary ToString(): a later version
    /// that understands the type would compute a different hash for the same data, silently splitting the
    /// corpus.
    /// </summary>
    [Fact]
    public void An_unknown_field_type_makes_the_key_partial()
    {
        var schema = new List<Field> { UniqueKey(InvoiceNoId, "mystery", "SomeFutureType") };

        FlexFieldFingerprintCalculator.Compute(Doc(("mystery", "value")), schema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    // --- Table (composite) keys, #625 ---

    private static readonly FieldConfigurationDictionary TableColumns = new TableConfiguration
    {
        Columns = new List<InlineFieldDefinition>
        {
            new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
            new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
        }
    }.ConfigurationDictionary;

    private static readonly List<Field> TableSchema = new()
    {
        UniqueKey(InvoiceNoId, "line_items", TableFieldType.ControlName, TableColumns)
    };

    private static List<TableRow> Rows(params (string Item, decimal Qty)[] rows)
        => rows.Select(r => new TableRow { Values = new FlexFieldDictionary { ["item"] = r.Item, ["qty"] = r.Qty } })
            .ToList();

    /// <summary>Each cell is canonicalized in column order, delegated to its own column's own extension.</summary>
    [Fact]
    public void Table_key_hashes_the_same_for_the_same_rows()
    {
        var doc = Doc(("line_items", Rows(("Widget", 3m), ("Gadget", 1.5m))));

        FlexFieldFingerprintCalculator.Compute(doc, TableSchema, TestFieldTypeRegistry.Default)
            .ShouldBe(FlexFieldFingerprintCalculator.Compute(doc, TableSchema, TestFieldTypeRegistry.Default));
    }

    [Fact]
    public void Table_key_changes_when_a_cell_changes()
    {
        var a = Doc(("line_items", Rows(("Widget", 3m))));
        var b = Doc(("line_items", Rows(("Widget", 4m))));

        FlexFieldFingerprintCalculator.Compute(a, TableSchema, TestFieldTypeRegistry.Default)
            .ShouldNotBe(FlexFieldFingerprintCalculator.Compute(b, TableSchema, TestFieldTypeRegistry.Default));
    }

    /// <summary>
    /// A row missing a cell makes the WHOLE Table key partial, not merely shorter - the same rule an
    /// ordinary unique-key field with no usable value follows.
    /// </summary>
    [Fact]
    public void A_table_row_missing_a_cell_makes_the_key_partial()
    {
        var rows = new List<TableRow> { new() { Values = new FlexFieldDictionary { ["item"] = "Widget" } } };
        var doc = Doc(("line_items", rows));

        FlexFieldFingerprintCalculator.Compute(doc, TableSchema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }

    [Fact]
    public void An_empty_table_makes_the_key_partial()
    {
        var doc = Doc(("line_items", new List<TableRow>()));

        FlexFieldFingerprintCalculator.Compute(doc, TableSchema, TestFieldTypeRegistry.Default).ShouldBeNull();
    }
}
