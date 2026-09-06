import { describe, expect, it } from 'vitest';

import { MULTI_VALUE_SEPARATOR, formatExtractedFieldValue } from './format-field-value';

// #501 item 6: the screen and the exported file must render a multi-value field identically. The server-side
// half of this pair is ExportCellRenderer.MultiValueSeparator, pinned by ExportCellRenderer_Tests. The constant
// cannot cross the language boundary, so each side names the literal and these two tests hold them in step.
describe('formatExtractedFieldValue', () => {
  it('joins a multi-value field with the same separator the exported file uses', () => {
    expect(formatExtractedFieldValue(['alpha', 'beta', 'gamma'])).toBe('alpha; beta; gamma');
  });

  it('pins the separator literal, which must equal ExportCellRenderer.MultiValueSeparator', () => {
    // Not a comma: a comma is the CSV delimiter, so the writer would quote the cell and a consumer re-splitting
    // it on commas would shred one field across several columns.
    expect(MULTI_VALUE_SEPARATOR).toBe('; ');
  });

  it('renders a single-element array without a trailing separator', () => {
    expect(formatExtractedFieldValue(['sole'])).toBe('sole');
  });

  it('renders an empty array as the em dash placeholder', () => {
    expect(formatExtractedFieldValue([])).toBe('—');
  });

  it('renders null and undefined as the em dash placeholder', () => {
    expect(formatExtractedFieldValue(null)).toBe('—');
    expect(formatExtractedFieldValue(undefined)).toBe('—');
  });

  it('renders scalars through String()', () => {
    expect(formatExtractedFieldValue('hello')).toBe('hello');
    expect(formatExtractedFieldValue(1000.5)).toBe('1000.5');
    expect(formatExtractedFieldValue(true)).toBe('true');
  });

  it('never lets an object surface as [object Object]', () => {
    expect(formatExtractedFieldValue({ a: 1 })).toBe('{"a":1}');
  });

  // #625: a Table field's egress value is a JSON array of row objects, not a flat array of scalars like
  // Tags / multi-Select. Array.isArray(value) is true for both shapes, so the per-element rendering has to
  // branch on the element's own shape rather than assuming every array element is a scalar.
  it('renders an array of plain strings the same as before (Tags / multi-Select)', () => {
    expect(formatExtractedFieldValue(['urgent', 'legal', '2026'])).toBe('urgent; legal; 2026');
  });

  it('renders array elements that are row objects as JSON, never [object Object] (Table, #625)', () => {
    const rows = [
      { item: 'Widget', qty: 3 },
      { item: 'Gadget', qty: 1.5 },
    ];

    expect(formatExtractedFieldValue(rows)).toBe('{"item":"Widget","qty":3}; {"item":"Gadget","qty":1.5}');
  });

  it('renders an empty Table value as the em dash placeholder, not "[]"', () => {
    expect(formatExtractedFieldValue([])).toBe('—');
  });
});
