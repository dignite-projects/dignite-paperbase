import type { FieldConfigurationDictionary } from '@dignite/vault-extract';

/**
 * One editable row of a Select field's option list. Mirrors the kernel's `SelectListItem`: `value` is the
 * stored value the extraction schema turns into a JSON-schema `enum` member, `text` is what an operator
 * reads.
 */
export interface SelectOptionRow {
  text: string;
  value: string;
}

const OPTIONS_KEY = 'Select.Options';

/**
 * Reads the option list out of a configuration bag. Tolerant on purpose: the value has been through JSON
 * on the way in, and a hand-edited or upconverted pack can put anything under the key. Anything that is
 * not an object with a usable `value` is dropped rather than rendered as a blank row the operator cannot
 * interpret.
 */
export function readSelectOptions(
  configuration: FieldConfigurationDictionary | null | undefined,
): SelectOptionRow[] {
  const raw = configuration?.[OPTIONS_KEY];
  if (!Array.isArray(raw)) {
    return [];
  }

  const rows: SelectOptionRow[] = [];
  for (const item of raw) {
    if (item === null || typeof item !== 'object') {
      continue;
    }

    // camelCase on the wire (JsonSerializerDefaults.Web), PascalCase if the bag was written in-process
    // and never round-tripped. Accept both rather than silently losing a list.
    const record = item as Record<string, unknown>;
    const value = record['value'] ?? record['Value'];
    const text = record['text'] ?? record['Text'];
    if (typeof value !== 'string' || !value) {
      continue;
    }

    rows.push({ value, text: typeof text === 'string' && text ? text : value });
  }

  return rows;
}

/**
 * Writes the option list back, dropping incomplete rows and de-duplicating by value.
 *
 * Both are load-bearing rather than tidiness: `FlexFieldValueReader` checks membership against this list
 * with no exception for an empty one, so a blank row would be an option nothing can ever match, and a
 * duplicate value would appear twice in the LLM's `enum`.
 */
export function writeSelectOptions(
  configuration: FieldConfigurationDictionary,
  rows: readonly SelectOptionRow[],
): FieldConfigurationDictionary {
  const seen = new Set<string>();
  const options: { text: string; value: string }[] = [];

  for (const row of rows) {
    const value = row.value.trim();
    if (!value || seen.has(value)) {
      continue;
    }

    seen.add(value);
    options.push({ value, text: row.text.trim() || value });
  }

  return { ...configuration, [OPTIONS_KEY]: options };
}
