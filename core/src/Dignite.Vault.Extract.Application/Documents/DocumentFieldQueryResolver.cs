using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Volo.Abp;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Resolves caller-supplied <see cref="DocumentFieldFilter"/>s into the kernel's
/// <see cref="FlexFieldQueryCondition"/>s, scoped to a single document type: each field name is looked up
/// and its value type taken from the field type itself, ready for
/// <c>IFlexFieldQueryExecutor&lt;Document&gt;</c>.
/// <para>
/// An unknown field name loud-fails with <see cref="VaultExtractErrorCodes.ExtractedField.Unknown"/> — a
/// correctable signal — instead of silently returning no rows. Shared by
/// <c>DocumentAppService.GetListAsync</c> (operator list + MCP search) and
/// <c>DocumentExportAppService.ExportAsync</c>, so that loud-fail cannot drift between the list and
/// export egress paths.
/// </para>
/// <para>
/// The value type comes from <c>IFieldType.IndexValueType</c> rather than from a switch here. That is the
/// same property the index manager types rows with, so a filter can never be compared against a slot the
/// value was not written into — and it means adding a field type does not add a case to this file.
/// </para>
/// </summary>
public static class DocumentFieldQueryResolver
{
    /// <summary>
    /// Resolves <paramref name="filters"/> against the live fields of <paramref name="documentTypeId"/>.
    /// <para>
    /// <paramref name="knownDefinitions"/> is an optional cache of fields the caller has already loaded for
    /// this same type — the export loads all of them to build its columns. A cache <b>hit</b> skips the
    /// round-trip; a <b>miss</b> still asks the repository before failing, because the cache is matched
    /// with <see cref="StringComparison.Ordinal"/> while <c>FindByNameAsync</c> compares in SQL, where the
    /// column's collation decides — SQL Server's default being case-insensitive. Loud-failing on an
    /// ordinal miss would reject a name the database, and the list path that has no cache, still accept.
    /// Only the database gets to say a field does not exist.
    /// </para>
    /// </summary>
    public static async Task<List<FlexFieldQueryCondition>> ResolveAsync(
        IFieldRepository fieldRepository,
        IFieldTypeResolver fieldTypeResolver,
        IReadOnlyList<DocumentFieldFilter> filters,
        Guid documentTypeId,
        string documentTypeCode,
        IReadOnlyList<Field>? knownDefinitions = null)
    {
        var conditions = new List<FlexFieldQueryCondition>(filters.Count);
        foreach (var filter in filters)
        {
            var definition =
                knownDefinitions?.FirstOrDefault(d => string.Equals(d.Name, filter.Name, StringComparison.Ordinal))
                ?? await fieldRepository.FindByNameAsync(documentTypeId, filter.Name!);

            if (definition == null)
            {
                throw new BusinessException(VaultExtractErrorCodes.ExtractedField.Unknown)
                    .WithData("FieldName", filter.Name!)
                    .WithData("DocumentTypeCode", documentTypeCode);
            }

            var valueType = fieldTypeResolver.Get(definition.FieldTypeName).IndexValueType;
            if (valueType == null)
            {
                // The field type keeps nothing in the query index — long text, under v2 as under v3 — so
                // there is no slot to compare against. Loud-fail rather than return an empty result set,
                // which would read as "no document matches" instead of "this field cannot be filtered on".
                throw new BusinessException(VaultExtractErrorCodes.ExtractedField.Unknown)
                    .WithData("FieldName", filter.Name!)
                    .WithData("DocumentTypeCode", documentTypeCode);
            }

            AddConditions(conditions, definition, valueType.Value, filter);
        }

        return conditions;
    }

    /// <summary>
    /// One filter becomes one equality condition, or up to two bound conditions for a range. The executor
    /// ANDs everything it is given, which is the same "different fields narrow each other, and so do the
    /// two ends of a range" semantics the v2 repository query had.
    /// </summary>
    private static void AddConditions(
        List<FlexFieldQueryCondition> conditions,
        Field definition,
        FlexFieldValueType valueType,
        DocumentFieldFilter filter)
    {
        if (filter.Value != null)
        {
            conditions.Add(new FlexFieldQueryCondition(
                definition.Id, definition.Name, FlexFieldQueryOperator.Equals,
                Normalize(filter.Value, valueType), valueType));
            return;
        }

        if (filter.Min != null)
        {
            conditions.Add(new FlexFieldQueryCondition(
                definition.Id, definition.Name, FlexFieldQueryOperator.GreaterThanOrEqual,
                Normalize(filter.Min, valueType), valueType));
        }

        if (filter.Max != null)
        {
            conditions.Add(new FlexFieldQueryCondition(
                definition.Id, definition.Name, FlexFieldQueryOperator.LessThanOrEqual,
                Normalize(filter.Max, valueType), valueType));
        }
    }

    /// <summary>
    /// Widens a bare date to the midnight instant the bag stores, so a caller can keep filtering a date
    /// field with <c>"2026-03-14"</c> the way it did under v2 even though Date and DateTime now share one
    /// field type and one <see cref="FlexFieldValueType.DateTime"/> slot. Anything else is passed through
    /// untouched for the kernel's converter to coerce.
    /// </summary>
    private static string Normalize(string raw, FlexFieldValueType valueType)
    {
        if (valueType == FlexFieldValueType.DateTime
            && DateOnly.TryParseExact(
                raw, FieldValueFormats.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToDateTime(TimeOnly.MinValue)
                .ToString(FieldValueFormats.DateTime, CultureInfo.InvariantCulture);
        }

        return raw;
    }
}
