import { describe, expect, it } from 'vitest';

import { FilterRow, composeFieldFilters, dateInputType, rangeSupported } from './field-value-filter.model';

// #415: the field-value composer compiles editor rows into server-shaped DocumentFieldFilter values. These
// guard the two rules the backend enforces: only Number and DateTime may carry a range, and every emitted
// filter must have at least one of value/min/max — an incomplete filter would be an AbpValidationException,
// and a range on Text/Boolean a FieldTypeDoesNotSupportRange error.

// Full FilterRow with defaults so each test states only what it exercises.
function row(overrides: Partial<FilterRow>): FilterRow {
  return {
    key: 0,
    fieldName: 'amount',
    fieldTypeName: 'Text',
    configuration: {},
    mode: 'eq',
    value: '',
    min: '',
    max: '',
    ...overrides,
  };
}

describe('rangeSupported', () => {
  // v2's Date and DateTime are one field type in v3, so what was three range-capable data types is two
  // field types. Nothing lost: both halves of the old split supported ranges.
  it('is true only for Number and DateTime', () => {
    expect(rangeSupported('Number')).toBe(true);
    expect(rangeSupported('DateTime')).toBe(true);
  });

  it('is false for Text / Boolean / LongText', () => {
    expect(rangeSupported('Text')).toBe(false);
    expect(rangeSupported('Boolean')).toBe(false);
    expect(rangeSupported('CKEditor')).toBe(false);
    expect(rangeSupported('Select')).toBe(false);
    expect(rangeSupported('Tags')).toBe(false);
  });
});

describe('composeFieldFilters', () => {
  it('emits a Text equality as { name, value } (no min/max)', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'partyName', fieldTypeName: 'Text', value: 'Acme' }),
    ]);
    expect(out).toEqual([{ name: 'partyName', value: 'Acme' }]);
  });

  it('emits a Boolean equality with the literal true/false string the server parses', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'signed', fieldTypeName: 'Boolean', value: 'false' }),
    ]);
    expect(out).toEqual([{ name: 'signed', value: 'false' }]);
  });

  it('emits Number equality (eq mode) as a value, not a range', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'amount', fieldTypeName: 'Number', mode: 'eq', value: '100' }),
    ]);
    expect(out).toEqual([{ name: 'amount', value: '100' }]);
  });

  it('keeps a literal "0" numeric equality (a falsy string must not be dropped)', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'amount', fieldTypeName: 'Number', mode: 'eq', value: '0' }),
    ]);
    expect(out).toEqual([{ name: 'amount', value: '0' }]);
  });

  it('keeps "0" range bounds (a "0" bound is a real bound, not an unset one)', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'amount', fieldTypeName: 'Number', mode: 'range', min: '0', max: '0' }),
    ]);
    expect(out).toEqual([{ name: 'amount', min: '0', max: '0' }]);
  });

  it('emits a two-sided Number range as { name, min, max } with no value', () => {
    const out = composeFieldFilters([
      row({ fieldName: 'amount', fieldTypeName: 'Number', mode: 'range', min: '10', max: '20' }),
    ]);
    expect(out).toEqual([{ name: 'amount', min: '10', max: '20' }]);
  });

  it('emits a one-sided range (unset bound becomes null)', () => {
    expect(
      composeFieldFilters([
        row({ fieldName: 'd', fieldTypeName: 'DateTime', mode: 'range', min: '2026-01-01' }),
      ]),
    ).toEqual([{ name: 'd', min: '2026-01-01', max: null }]);
    expect(
      composeFieldFilters([
        row({ fieldName: 'd', fieldTypeName: 'DateTime', mode: 'range', max: '2026-12-31' }),
      ]),
    ).toEqual([{ name: 'd', min: null, max: '2026-12-31' }]);
  });

  it('drops a row with no field selected', () => {
    expect(composeFieldFilters([row({ fieldName: '', value: 'x' })])).toEqual([]);
  });

  it('drops an equality row whose value is blank (never sends an incomplete filter)', () => {
    expect(
      composeFieldFilters([row({ fieldName: 'amount', fieldTypeName: 'Text', value: '   ' })]),
    ).toEqual([]);
  });

  it('drops a range row with neither bound', () => {
    expect(
      composeFieldFilters([
        row({ fieldName: 'amount', fieldTypeName: 'Number', mode: 'range', min: ' ', max: '' }),
      ]),
    ).toEqual([]);
  });

  it('trims values and bounds', () => {
    expect(
      composeFieldFilters([row({ fieldName: 'a', fieldTypeName: 'Text', value: '  hi  ' })]),
    ).toEqual([{ name: 'a', value: 'hi' }]);
    expect(
      composeFieldFilters([
        row({ fieldName: 'n', fieldTypeName: 'Number', mode: 'range', min: ' 1 ', max: ' 9 ' }),
      ]),
    ).toEqual([{ name: 'n', min: '1', max: '9' }]);
  });

  it('never builds a range on a non-range-capable type — falls back to equality', () => {
    // The UI never offers range mode on Text, but even if a row arrives in range mode the compiler must
    // not emit a Text range (the server hard-errors it); it compiles the equality value instead.
    const out = composeFieldFilters([
      row({ fieldName: 't', fieldTypeName: 'Text', mode: 'range', value: 'v', min: '1', max: '2' }),
    ]);
    expect(out).toEqual([{ name: 't', value: 'v' }]);
  });

  it('preserves order across multiple rows', () => {
    const out = composeFieldFilters([
      row({ key: 1, fieldName: 'a', fieldTypeName: 'Text', value: 'x' }),
      row({ key: 2, fieldName: 'b', fieldTypeName: 'Number', mode: 'range', min: '1', max: '2' }),
    ]);
    expect(out).toEqual([
      { name: 'a', value: 'x' },
      { name: 'b', min: '1', max: '2' },
    ]);
  });
});

// Date, DateTime and Month are one field type told apart by DateTime.InputMode, and each needs the native
// input whose value shape the server parses back for that mode. Month is the one that regressed: it used
// to fall through to datetime-local, so filtering a month field asked for a day and a time it never stores.
describe('dateInputType', () => {
  it('maps each DateTime.InputMode to the input producing that mode’s value shape', () => {
    expect(dateInputType({ 'DateTime.InputMode': 0 as unknown as object })).toBe('date');
    expect(dateInputType({ 'DateTime.InputMode': 1 as unknown as object })).toBe('datetime-local');
    expect(dateInputType({ 'DateTime.InputMode': 2 as unknown as object })).toBe('month');
  });

  it('falls back to datetime-local when the mode is absent', () => {
    // An unconfigured DateTime field is a full timestamp — the widest of the three, so a filter built on
    // it can still express the other two rather than silently dropping precision the field may hold.
    expect(dateInputType({})).toBe('datetime-local');
    expect(dateInputType(null)).toBe('datetime-local');
    expect(dateInputType(undefined)).toBe('datetime-local');
  });
});
