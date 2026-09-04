import { DocumentFieldFilter } from '@dignite/ng.vault-extract';

export type FilterMode = 'eq' | 'range';

// One in-progress editor row for the field-value composer. Deliberately richer than the server contract
// (DocumentFieldFilter): it carries the resolved field type + mode so the UI can render the right input, and
// is compiled down to DocumentFieldFilter only on Apply, dropping incomplete rows.
export interface FilterRow {
  key: number;
  fieldName: string;
  fieldTypeName: string;

  /** The field type's configuration, carried so the editor can tell a date-only DateTime from a full one. */
  configuration: Record<string, object>;
  mode: FilterMode;
  value: string;
  min: string;
  max: string;
}

/**
 * Only Number and DateTime support ranges. Text / Boolean / Select / Tags are equality-only (the server
 * hard-errors a range on them), and long text is not queryable at all.
 *
 * Date and DateTime were two v2 data types and are one field type in v3, told apart by configuration —
 * which does not matter here, because both ends of that split supported ranges.
 */
export function rangeSupported(fieldTypeName: string | null | undefined): boolean {
  return fieldTypeName === 'Number' || fieldTypeName === 'DateTime';
}

/**
 * The `<input type>` a DateTime field's filter uses — the distinction v2 carried as two separate data
 * types and v3 carries in configuration, plus Month, which v2 had no equivalent for.
 *
 * Each input produces exactly the shape the server parses back for that mode (`yyyy-MM-dd`,
 * `yyyy-MM-ddTHH:mm`, `yyyy-MM`), which is the whole reason this maps all three rather than answering a
 * date-or-not boolean: Month used to fall through to `datetime-local`, so filtering a month field asked
 * the operator for a day and a time the field does not store.
 *
 * `Number(...)`: a config value is typed `object` (the server's `Dictionary<string,object>` as the proxy
 * generator sees it), even though this one is stored as a number at runtime.
 */
export function dateInputType(configuration: Record<string, object> | null | undefined): string {
  // Mirrors DateTimeInputMode on the server: Date = 0, DateTime = 1, Month = 2.
  switch (Number(configuration?.['DateTime.InputMode'])) {
    case 0:
      return 'date';
    case 2:
      return 'month';
    default:
      return 'datetime-local';
  }
}

/**
 * Whether a field can appear in a field-value filter. False for a type that indexes nothing, and for a
 * field whose searchability the admin turned off — filtering on either is rejected server-side, so the
 * filter UI leaves them out rather than offering a choice that errors.
 *
 * `indexableByFieldType` comes from the server (FieldDefinitionAppService.GetFieldTypesAsync /
 * IFieldType.IndexValueType) rather than a client-side type catalog: whether a type indexes anything is
 * that service's call to make, not this UI's to restate. Cross-checking it here (rather than trusting a
 * field's own `isSearchable` alone) also catches a field left over from before searchability was
 * validated against its type server-side.
 */
export function isFilterableField(
  typeName: string | null | undefined,
  isSearchable: boolean | null | undefined,
  indexableByFieldType: ReadonlyMap<string, boolean>,
): boolean {
  return (isSearchable ?? true) && (indexableByFieldType.get(typeName ?? '') ?? false);
}

/**
 * Compile editor rows into server-shaped {@link DocumentFieldFilter} values. Incomplete rows — no field
 * chosen, or no value / no bound entered — are dropped so the request never trips the server's "at least
 * one of value/min/max" guard (which would otherwise be an AbpValidationException). A range is emitted only
 * for range-capable types; the rest always compile to equality, so a range (rejected server-side for them)
 * can never be built. Values are trimmed and emitted as strings exactly as the server parsers expect.
 */
export function composeFieldFilters(rows: readonly FilterRow[]): DocumentFieldFilter[] {
  const filters: DocumentFieldFilter[] = [];
  for (const r of rows) {
    if (!r.fieldName) {
      continue;
    }
    if (r.mode === 'range' && rangeSupported(r.fieldTypeName)) {
      const min = r.min.trim();
      const max = r.max.trim();
      if (min || max) {
        filters.push({ name: r.fieldName, min: min || null, max: max || null });
      }
    } else {
      const value = r.value.trim();
      if (value) {
        filters.push({ name: r.fieldName, value });
      }
    }
  }
  return filters;
}
