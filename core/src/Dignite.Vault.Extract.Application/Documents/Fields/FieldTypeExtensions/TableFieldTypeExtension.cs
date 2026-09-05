using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Table;
using Dignite.Vault.Extract.FlexFields;
using Volo.Abp.DependencyInjection;

namespace Dignite.Vault.Extract.Documents.Fields.FieldTypeExtensions;

/// <summary>
/// The kernel's composite grid type: one shared column schema (<see cref="TableConfiguration.Columns"/>)
/// applied to every row. Not indexable (<c>IndexValueType</c> is null, like <c>CKEditor</c>).
/// <para>
/// Its <c>List&lt;TableRow&gt;</c> value is one opaque composite scalar to every shared dispatcher in this
/// family, never multi-valued in the "repeated scalars" sense <c>Tags</c>/multi-<c>Select</c> are -
/// <see cref="IsMultiValue"/> is unconditionally <c>false</c>. It has to be: the shared dispatchers
/// (<c>FlexFieldValueJsonWriter</c> in particular) intercept a multi-valued field before any per-type
/// dispatch and read its value as a flat <c>List&lt;string&gt;</c>, which a table's rows are not.
/// </para>
/// <para>
/// Every cell is delegated recursively to its own column's own registered
/// <see cref="IVaultExtractFieldTypeExtension"/>, resolved through <see cref="Registry"/> - see that
/// member's own doc for why it is not simply constructor-injected. <c>Matrix</c> is out of scope (#625).
/// </para>
/// </summary>
public class TableFieldTypeExtension : VaultExtractFieldTypeExtensionBase
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private IVaultExtractFieldTypeRegistry? _registry;

    /// <summary>
    /// The registry used to resolve each column's own extension. Settable directly, bypassing
    /// <see cref="VaultExtractFieldTypeExtensionBase.LazyServiceProvider"/> entirely - for a hand-built
    /// registry with no DI container (<c>TestFieldTypeRegistry</c>, the Mcp.Tests inline arrays). Nothing
    /// sets it in a real host, so the getter falls back to resolving it lazily from
    /// <see cref="VaultExtractFieldTypeExtensionBase.LazyServiceProvider"/> on first use instead - the same
    /// deferral that avoids the constructor-time cycle described there.
    /// <para>
    /// <see cref="DisablePropertyInjectionAttribute"/> is required, not optional: ABP's conventional
    /// property autowiring (<c>AbpPropertySelector</c>) auto-wires <b>every</b> public settable property
    /// whose type is resolvable in the container, not just <see cref="VaultExtractFieldTypeExtensionBase.LazyServiceProvider"/>
    /// by name. Without this attribute, Autofac would eagerly resolve
    /// <see cref="IVaultExtractFieldTypeRegistry"/> as part of activating this very instance - reintroducing,
    /// through property injection, the exact constructor-time cycle <see cref="VaultExtractFieldTypeExtensionBase.LazyServiceProvider"/>
    /// exists to avoid (verified: without this attribute, resolving this type through a real ABP/Autofac
    /// container stack-overflows).
    /// </para>
    /// </summary>
    [DisablePropertyInjection]
    public IVaultExtractFieldTypeRegistry Registry {
        get => _registry ??= LazyServiceProvider.LazyGetRequiredService<IVaultExtractFieldTypeRegistry>();
        set => _registry = value;
    }

    public override string FieldTypeName => TableFieldType.ControlName;

    public override bool IsMultiValue(FieldConfigurationDictionary? configuration) => false;

    public override bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result)
    {
        result = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var columns = new TableConfiguration(configuration).Columns;
        var columnNames = columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var registry = Registry;
        var rows = new List<TableRow>();

        foreach (var rowElement in value.EnumerateArray())
        {
            // Strict, like every other type's TryRead: a row is an object carrying exactly the declared
            // columns - never a bare scalar, and never a key no column declares.
            if (rowElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in rowElement.EnumerateObject())
            {
                if (!columnNames.Contains(property.Name))
                {
                    return false;
                }
            }

            var row = new TableRow();
            foreach (var column in columns)
            {
                var hasCell = rowElement.TryGetProperty(column.Name, out var cellElement)
                              && cellElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

                if (!hasCell)
                {
                    // The whole table is rejected rather than accepted with a hole: a row missing a
                    // Required column is not a complete extraction, the same reject-the-whole-group
                    // philosophy Tags/Select already apply to their own elements.
                    if (column.Required)
                    {
                        return false;
                    }

                    continue;
                }

                if (!registry.TryGet(column.FieldTypeName, out var columnExtension) ||
                    !columnExtension!.TryRead(cellElement, column.Configuration, out var cellValue))
                {
                    return false;
                }

                if (cellValue != null)
                {
                    row.Values[column.Name] = cellValue;
                }
            }

            rows.Add(row);
        }

        result = rows;
        return true;
    }

    public override JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration)
    {
        var columns = new TableConfiguration(configuration).Columns;
        var registry = Registry;

        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var column in columns)
        {
            // Last-resort defense, mirroring FlexFieldValueSchemaBuilder.Build's own loud failure for an
            // unregistered outer field type - should be unreachable, because
            // FieldDefinitionAppService.EnsureFieldTypeRegistered already rejects an unregistered column
            // type at field-definition create/update time, before a Table field carrying one can be saved.
            if (!registry.TryGet(column.FieldTypeName, out var columnExtension))
            {
                throw new NotSupportedException(
                    $"No extraction schema is defined for field type '{column.FieldTypeName}' (Table column '{column.Name}').");
            }

            properties[column.Name] = columnExtension!.BuildExtractionSchema(column.Configuration);
            if (column.Required)
            {
                required.Add(column.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = new JsonArray("array", "null"),
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            },
            ["description"] = "A JSON array of row objects, one per table row, or null/empty array when absent."
        };
    }

    public override JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration)
        => JsonSerializer.SerializeToElement(BuildEgressRows(value, configuration));

    /// <summary>
    /// Flattens the rows to a compact JSON string - a reversible, non-persisted rendering choice (#625):
    /// the export cell gets valid, readable JSON rather than a per-row <c>ToString()</c>. Reuses
    /// <see cref="WriteJson"/> rather than re-deriving the same per-column recursion, since the egress
    /// shape and the export shape are the same JSON here, just carried as a string instead of a
    /// <see cref="JsonElement"/>.
    /// </summary>
    public override string? RenderForExport(object value, FieldConfigurationDictionary configuration)
        => WriteJson(value, configuration)?.GetRawText();

    public override IReadOnlyList<string> CanonicalizeForFingerprint(object value, FieldConfigurationDictionary configuration)
    {
        var columns = new TableConfiguration(configuration).Columns;
        var registry = Registry;
        var rows = ReadStoredRows(value);

        var canonical = new List<string>();
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                if (!row.Values.TryGetValue(column.Name, out var cellValue) || cellValue == null ||
                    !registry.TryGet(column.FieldTypeName, out var columnExtension))
                {
                    // Same partial-key rule FieldTypeExtensionHelpers.CanonicalizeListForFingerprint
                    // enforces for Tags/multi-Select: one cell that fails to normalize makes the WHOLE
                    // field's contribution empty, never a shorter one - a missing cell is not "one fewer
                    // character" of the key, it is "this key is incomplete".
                    return Array.Empty<string>();
                }

                var cellCanonical = columnExtension!.CanonicalizeForFingerprint(cellValue, column.Configuration);
                if (cellCanonical.Count == 0)
                {
                    return Array.Empty<string>();
                }

                canonical.AddRange(cellCanonical);
            }
        }

        return canonical;
    }

    private List<Dictionary<string, JsonElement>> BuildEgressRows(object value, FieldConfigurationDictionary configuration)
    {
        var columns = new TableConfiguration(configuration).Columns;
        var registry = Registry;
        var rows = ReadStoredRows(value);

        var payload = new List<Dictionary<string, JsonElement>>(rows.Count);
        foreach (var row in rows)
        {
            var rowPayload = new Dictionary<string, JsonElement>();
            foreach (var column in columns)
            {
                if (!row.Values.TryGetValue(column.Name, out var cellValue) || cellValue == null ||
                    !registry.TryGet(column.FieldTypeName, out var columnExtension))
                {
                    // An unregistered column type should be impossible by the time a value reaches here -
                    // FieldDefinitionAppService.EnsureFieldTypeRegistered rejects it at save time. Skipping
                    // the cell defensively here, rather than throwing, keeps a document's other columns
                    // readable even if its schema somehow drifted after the fact (e.g. a downstream removed
                    // its own extension for a type it had previously registered).
                    continue;
                }

                var rendered = columnExtension!.WriteJson(cellValue, column.Configuration);
                if (rendered != null)
                {
                    rowPayload[column.Name] = rendered.Value;
                }
            }

            payload.Add(rowPayload);
        }

        return payload;
    }

    /// <summary>
    /// Reads a stored value across the CLR/reloaded-<see cref="JsonElement"/> shape split every field type
    /// here handles - a fresh <see cref="List{TableRow}"/> in memory, or a <see cref="JsonElement"/> array
    /// of row objects after a bag reload. <see cref="JsonSerializerDefaults.Web"/> (case-insensitive)
    /// absorbs whatever casing convention the bag's own JSON column serializer used for
    /// <see cref="TableRow.Values"/>.
    /// </summary>
    private static List<TableRow> ReadStoredRows(object value) => value switch
    {
        List<TableRow> rows => rows,
        JsonElement { ValueKind: JsonValueKind.Array } element =>
            element.Deserialize<List<TableRow>>(WebOptions) ?? new List<TableRow>(),
        _ => new List<TableRow>()
    };
}
