using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.Documents.Fields.Migration;

/// <summary>
/// Turns a document's v2 <see cref="DocumentExtractedField"/> rows into the v3 value bag (#561 step 4).
/// <para>
/// Pure, so the shape of the migrated bag is testable without a database — which matters more here than
/// usual, because a wrong shape is not a crash: it is a document that quietly stops matching the filters
/// it used to match.
/// </para>
/// </summary>
public static class FieldValueBagBuilder
{
    /// <summary>
    /// Builds the bag for one document.
    /// </summary>
    /// <param name="values">The document's field-value rows, in any order.</param>
    /// <param name="definitions">
    /// The definitions those rows point at, used for the field name (the bag's key) and the data type
    /// (which typed column to read). A row whose definition is missing — a soft-deleted definition whose
    /// rows outlived it — is skipped rather than guessed at.
    /// </param>
    public static FlexFieldDictionary Build(
        IReadOnlyCollection<DocumentExtractedField> values,
        IReadOnlyCollection<Field> definitions)
    {
        var bag = new FlexFieldDictionary();
        var byId = definitions.ToDictionary(d => d.Id);

        foreach (var group in values.GroupBy(v => v.FieldDefinitionId))
        {
            if (!byId.TryGetValue(group.Key, out var definition))
            {
                continue;
            }

            // Ascending Order is the multi-value contract, and it is load-bearing twice over: it is the
            // order the operator sees, and it is the order FieldFingerprintCalculator hashes in. A bag
            // built in row-enumeration order would produce a different fingerprint for the same data.
            var ordered = group.OrderBy(v => v.Order).ToList();

            var isMultiValued = IsMultiValued(definition);
            if (isMultiValued)
            {
                // A list even when it currently holds one element: the field is multi-valued by type, and
                // a bag that stores a bare scalar for a one-element set would read back as a scalar
                // forever, silently changing the field's shape on the egress.
                bag[definition.Name] = ordered.Select(ReadText).Where(v => v != null).ToList()!;
                continue;
            }

            var row = ordered[0];
            var value = ReadScalar(row, definition);
            if (value != null)
            {
                bag[definition.Name] = value;
            }
        }

        return bag;
    }

    /// <summary>
    /// Whether the v3 field type stores a list. Keyed on the migrated <c>FieldTypeName</c> rather than on
    /// v2's <c>AllowMultiple</c>, because after the migration the type is the only thing that says so.
    /// </summary>
    private static bool IsMultiValued(Field definition)
    {
        return string.Equals(definition.FieldTypeName, FlexFields.Tags.TagsFieldType.ControlName, StringComparison.Ordinal);
    }

    private static string? ReadText(DocumentExtractedField row) => row.TextValue ?? row.LongTextValue;

    /// <summary>
    /// Reads the one populated typed column into the CLR value the bag should carry.
    /// <para>
    /// Deliberately plain values rather than <c>JsonElement</c>s: the bag is serialized to JSON on save,
    /// and storing already-parsed elements would round-trip a document's values through a shape that only
    /// happens to work. A <c>Date</c> lands as midnight, which is what makes an equality filter on a date
    /// stay an equality filter after Date and DateTime merge into one field type (#559 resolution 5).
    /// </para>
    /// </summary>
    private static object? ReadScalar(DocumentExtractedField row, Field definition)
    {
        if (row.TextValue != null) return row.TextValue;
        if (row.LongTextValue != null) return row.LongTextValue;
        if (row.NumberValue != null) return row.NumberValue.Value;
        if (row.BooleanValue != null) return row.BooleanValue.Value;
        if (row.DateValue != null) return row.DateValue.Value.ToDateTime(TimeOnly.MinValue);
        if (row.DateTimeValue != null) return row.DateTimeValue.Value;

        // Every column null means the row carries no value at all. Nothing to migrate, and writing an
        // explicit null into the bag would make "extracted as empty" indistinguishable from "not
        // extracted" for a field that was simply never filled.
        return null;
    }
}
