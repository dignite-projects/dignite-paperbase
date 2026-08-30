import { DocumentFieldFilter, FieldConfigurationDictionary } from "@dignite/vault-extract";
import { FIELD_TYPES } from '../field-types/field-type-catalog';

export type FilterMode = 'eq' | 'range';

// One in-progress editor row for the field-value composer. Deliberately richer than the server contract
// (DocumentFieldFilter): it carries the resolved field type + mode so the UI can render the right input, and
// is compiled down to DocumentFieldFilter only on Apply, dropping incomplete rows.
export interface FilterRow {
  key: number;
  fieldName: string;
  fieldTypeName: string;

  /** The field type's configuration, carried so the editor can tell a date-only DateTime from a full one. */
  configuration: FieldConfigurationDictionary;
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
  return fieldTypeName === FIELD_TYPES.number || fieldTypeName === FIELD_TYPES.dateTime;
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
