import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe } from '@abp/ng.core';
import { DocumentFieldFilter, FieldDefinitionDto } from '@dignite/vault-extract';
import { FIELD_TYPES, isDateOnly, isFilterableField } from '../field-types/field-type-catalog';

import {
  FilterMode,
  FilterRow,
  composeFieldFilters,
  rangeSupported,
} from './field-value-filter.model';

// Client mirror of the server caps (DocumentConsts). The server re-validates both — these only keep the
// UI from composing a request that would trip the guard, so an operator gets inline limits instead of a
// 400. Keep in sync with DocumentConsts.MaxSearchFieldFilters / MaxSearchFieldValueLength.
const MAX_FIELD_FILTERS = 10;
const MAX_FIELD_VALUE_LENGTH = 512;

/**
 * #415: reusable extracted-field-value filter composer. Given a single document type's field definitions,
 * it lets the operator build a type-scoped, data-type-aware set of {@link DocumentFieldFilter} rows and
 * emits them (AND-combined server-side) on Apply. It owns no query state and no backend call — the parent
 * decides what to do with the emitted filters — so the same component backs both the operator document
 * list (this issue) and the Data Download surface (#414).
 *
 * Operator/input is driven by the field type, matching what the backend's field-value filter supports:
 * Text/Boolean → equality only; Number/Date/DateTime → equality or inclusive (one- or two-sided) range;
 * LongText is excluded from the picker entirely (the backend loud-fails a LongText filter by design).
 */
@Component({
  selector: 'lib-field-value-filter',
  templateUrl: './field-value-filter.component.html',
  styleUrls: ['./field-value-filter.component.scss'],
  standalone: true,
  imports: [CommonModule, FormsModule, LocalizationPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FieldValueFilterComponent {
  private nextKey = 0;

  private readonly _fields = signal<FieldDefinitionDto[]>([]);

  // The selected type's field definitions. Assigning a new set (i.e. the operator switched document type)
  // clears the composer: the previous type's fields no longer apply, so stale rows must neither linger in
  // the UI nor be emitted. This clear is silent — the parent clears its own applied-filter state on the
  // same type change and re-queries once, so there is no redundant fetch here.
  @Input() set fieldDefinitions(value: FieldDefinitionDto[] | null | undefined) {
    this._fields.set(value ?? []);
    this.rows.set([]);
  }

  // Emitted only on Apply (the composed, server-shaped filters) or Clear (empty). Deliberately NOT emitted
  // per keystroke: the consumer re-queries on each emit, so per-edit emission would spam the list endpoint.
  @Output() filtersChange = new EventEmitter<DocumentFieldFilter[]>();

  readonly FIELD_TYPES = FIELD_TYPES;
  readonly maxValueLength = MAX_FIELD_VALUE_LENGTH;

  readonly rows = signal<FilterRow[]>([]);

  // A field the server would reject as a filter never appears in the picker: long text indexes nothing,
  // and neither does a field whose admin turned searchability off. Both loud-fail server-side, so offering
  // them here would only turn a choice into an error.
  readonly filterableFields = computed(() =>
    this._fields().filter(f => isFilterableField(f.fieldTypeName, f.isSearchable)),
  );

  readonly hasFilterableFields = computed(() => this.filterableFields().length > 0);
  readonly canAddRow = computed(() => this.rows().length < MAX_FIELD_FILTERS);

  addRow(): void {
    if (!this.canAddRow()) {
      return;
    }
    this.rows.update(rows => [
      ...rows,
      {
        key: this.nextKey++,
        fieldName: '',
        fieldTypeName: FIELD_TYPES.text,
        configuration: {},
        mode: 'eq',
        value: '',
        min: '',
        max: '',
      },
    ]);
  }

  removeRow(key: number): void {
    this.rows.update(rows => rows.filter(r => r.key !== key));
  }

  // Picking a field resets the row's operator + inputs: the new field's type may not support the old
  // mode (e.g. switching to a Text field while in range mode), and a carried-over value would be
  // mistyped. Start clean at equality.
  onFieldChange(key: number, fieldName: string): void {
    const field = this.filterableFields().find(f => f.name === fieldName);
    const fieldTypeName = field?.fieldTypeName ?? FIELD_TYPES.text;
    // The row carries the configuration too, because Date and DateTime are one field type in v3 and only
    // its InputMode says which of the two a given field is - and that decides the input control.
    const configuration = field?.configuration ?? {};
    this.rows.update(rows =>
      rows.map(r =>
        r.key === key
          ? { ...r, fieldName, fieldTypeName, configuration, mode: 'eq', value: '', min: '', max: '' }
          : r,
      ),
    );
  }

  onModeChange(key: number, mode: FilterMode): void {
    this.rows.update(rows =>
      rows.map(r => (r.key === key ? { ...r, mode, value: '', min: '', max: '' } : r)),
    );
  }

  patchRow(key: number, patch: Partial<Pick<FilterRow, 'value' | 'min' | 'max'>>): void {
    this.rows.update(rows => rows.map(r => (r.key === key ? { ...r, ...patch } : r)));
  }

  // A `type="number"` input binds through Angular's NumberValueAccessor, whose ngModelChange emits a
  // `number | null`, not a string; date/datetime-local/text emit strings. Coerce uniformly so the row
  // stays string-typed and composeFilters' trim() never runs against a number. Number.toString() is
  // invariant (dot decimal) — exactly what the server's ParseDecimal expects.
  coerce(value: unknown): string {
    return value === null || value === undefined ? '' : String(value);
  }

  // Kept as a method (delegating to the pure rangeSupported) so the template can call it directly.
  supportsRange(fieldTypeName: string): boolean {
    return rangeSupported(fieldTypeName);
  }

  // Native input type per field type. The resulting string values are exactly what the server parser
  // expects: number -> invariant decimal, date -> yyyy-MM-dd, datetime-local -> offset-free wall-clock
  // (Kind=Unspecified), which is what the field-value filter requires.
  //
  // The date split is the one place v3's merged DateTime type shows through: v2 had two data types to
  // switch on, v3 has one plus an InputMode, so the row's configuration decides.
  inputType(row: Pick<FilterRow, 'fieldTypeName' | 'configuration'>): string {
    switch (row.fieldTypeName) {
      case FIELD_TYPES.number:
        return 'number';
      case FIELD_TYPES.dateTime:
        return isDateOnly(row.configuration) ? "date" : "datetime-local";
      default:
        return 'text';
    }
  }

  // Emit the composed, server-shaped filters (dropping incomplete rows — see composeFieldFilters).
  apply(): void {
    this.filtersChange.emit(composeFieldFilters(this.rows()));
  }

  clear(): void {
    this.rows.set([]);
    this.filtersChange.emit([]);
  }
}
