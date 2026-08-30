import type { FieldConfigurationDictionary } from '@dignite/vault-extract';

/**
 * Registration keys of the field types Vault Extract offers. Frozen strings, not a TypeScript enum:
 * the server stores this exact value on every field and dispatches on it, so a key is wire format.
 */
export const FIELD_TYPES = {
  text: 'Text',
  number: 'Number',
  boolean: 'Boolean',
  dateTime: 'DateTime',
  select: 'Select',
  ckEditor: 'CKEditor',
  tags: 'Tags',
} as const;

export type FieldTypeName = (typeof FIELD_TYPES)[keyof typeof FIELD_TYPES];

/**
 * One editable configuration item of a field type. `key` is the server-side configuration name verbatim
 * ("Text.CharLimit"), because that is the dictionary key the value is stored under.
 */
export interface FieldConfigurationOption {
  key: string;

  /** Localization key suffix; rendered as `::FieldConfig:{labelKey}`. */
  labelKey: string;

  kind: 'text' | 'number' | 'boolean' | 'choice' | 'date' | 'options';

  /** For `kind: 'choice'` — the numeric value each choice writes, and its label suffix. */
  choices?: { value: number; labelKey: string }[];

  /** Written when the operator leaves the control empty, and preselected on a fresh field. */
  default?: unknown;

  min?: number;
  max?: number;
}

export interface FieldTypeDescriptor {
  name: FieldTypeName;

  /** Localization key suffix; rendered as `::FieldType:{labelKey}`. */
  labelKey: string;

  /**
   * Whether values of this type reach the query index, and can therefore be filtered on. Mirrors
   * `IFieldType.IndexValueType != null` server-side: long text is the one type that indexes nothing, so
   * its `isSearchable` is meaningless and the editor says so rather than offering a dead switch.
   */
  indexable: boolean;

  /**
   * Whether this type always holds a list. Select is deliberately absent: it is multi-valued only when
   * its own `Select.Multiple` says so, which is why "is this field multi-valued" is a function of type
   * *and* configuration — see `isMultiValueField`.
   */
  alwaysMultiValue?: boolean;

  configuration: FieldConfigurationOption[];
}

// Enum ordinals, matching the C# enums. Numbers rather than names because the server reads a
// configuration enum through `(int)(long)value`; a string name takes the fallback path, where
// JsonSerializerDefaults.Web deserializes enums numerically and a name fails.
const TEXT_MODE = { singleLine: 0, multipleLine: 1 };
const DATE_TIME_INPUT_MODE = { date: 0, dateTime: 1, month: 2 };
const CKEDITOR_MODE = { basic: 0, full: 1 };
const CKEDITOR_CONTENT_FORMAT = { html: 0, markdown: 1 };

export const FIELD_TYPE_CATALOG: readonly FieldTypeDescriptor[] = [
  {
    name: FIELD_TYPES.text,
    labelKey: 'Text',
    indexable: true,
    configuration: [
      {
        key: 'Text.Mode',
        labelKey: 'TextMode',
        kind: 'choice',
        default: TEXT_MODE.singleLine,
        choices: [
          { value: TEXT_MODE.singleLine, labelKey: 'TextMode:SingleLine' },
          { value: TEXT_MODE.multipleLine, labelKey: 'TextMode:MultipleLine' },
        ],
      },
      { key: 'Text.CharLimit', labelKey: 'CharLimit', kind: 'number', default: 4000, min: 1 },
      { key: 'Text.Placeholder', labelKey: 'Placeholder', kind: 'text' },
    ],
  },
  {
    name: FIELD_TYPES.number,
    labelKey: 'Number',
    indexable: true,
    configuration: [
      { key: 'Number.Decimals', labelKey: 'Decimals', kind: 'number', default: 0, min: 0, max: 6 },
      { key: 'Number.Min', labelKey: 'Min', kind: 'number' },
      { key: 'Number.Max', labelKey: 'Max', kind: 'number' },
      { key: 'Number.Step', labelKey: 'Step', kind: 'number' },
      { key: 'FormatSpecifier', labelKey: 'FormatSpecifier', kind: 'text' },
    ],
  },
  {
    name: FIELD_TYPES.boolean,
    labelKey: 'Boolean',
    indexable: true,
    configuration: [{ key: 'Boolean.Default', labelKey: 'BooleanDefault', kind: 'boolean', default: false }],
  },
  {
    name: FIELD_TYPES.dateTime,
    labelKey: 'DateTime',
    indexable: true,
    configuration: [
      {
        key: 'DateTime.InputMode',
        labelKey: 'DateTimeInputMode',
        kind: 'choice',
        default: DATE_TIME_INPUT_MODE.date,
        choices: [
          { value: DATE_TIME_INPUT_MODE.date, labelKey: 'DateTimeInputMode:Date' },
          { value: DATE_TIME_INPUT_MODE.dateTime, labelKey: 'DateTimeInputMode:DateTime' },
          { value: DATE_TIME_INPUT_MODE.month, labelKey: 'DateTimeInputMode:Month' },
        ],
      },
      { key: 'DateTime.Min', labelKey: 'Min', kind: 'date' },
      { key: 'DateTime.Max', labelKey: 'Max', kind: 'date' },
    ],
  },
  {
    name: FIELD_TYPES.select,
    labelKey: 'Select',
    indexable: true,
    configuration: [
      // The one option that decides array-vs-scalar for this type, which is why it leads.
      { key: 'Select.Multiple', labelKey: 'SelectMultiple', kind: 'boolean', default: false },
      { key: 'Select.Options', labelKey: 'SelectOptions', kind: 'options', default: [] },
      { key: 'Select.NullText', labelKey: 'SelectNullText', kind: 'text' },
      { key: 'Select.Size', labelKey: 'SelectSize', kind: 'number', min: 1 },
    ],
  },
  {
    name: FIELD_TYPES.ckEditor,
    labelKey: 'LongText',
    indexable: false,
    configuration: [
      {
        key: 'CKEditor.ContentFormat',
        labelKey: 'ContentFormat',
        kind: 'choice',
        // Markdown, not the type's own Html default: these values are text extracted from a document.
        default: CKEDITOR_CONTENT_FORMAT.markdown,
        choices: [
          { value: CKEDITOR_CONTENT_FORMAT.markdown, labelKey: 'ContentFormat:Markdown' },
          { value: CKEDITOR_CONTENT_FORMAT.html, labelKey: 'ContentFormat:Html' },
        ],
      },
      {
        key: 'CKEditor.Mode',
        labelKey: 'EditorMode',
        kind: 'choice',
        default: CKEDITOR_MODE.basic,
        choices: [
          { value: CKEDITOR_MODE.basic, labelKey: 'EditorMode:Basic' },
          { value: CKEDITOR_MODE.full, labelKey: 'EditorMode:Full' },
        ],
      },
      { key: 'CKEditor.InitialContent', labelKey: 'InitialContent', kind: 'text' },
    ],
  },
  {
    name: FIELD_TYPES.tags,
    labelKey: 'Tags',
    indexable: true,
    alwaysMultiValue: true,
    configuration: [
      { key: 'Tags.MaxCount', labelKey: 'MaxCount', kind: 'number', default: 100, min: 1 },
      { key: 'Tags.MaxLength', labelKey: 'MaxLength', kind: 'number', default: 256, min: 1 },
      { key: 'Tags.Placeholder', labelKey: 'Placeholder', kind: 'text' },
    ],
  },
];

export function findFieldType(name: string | null | undefined): FieldTypeDescriptor | undefined {
  return FIELD_TYPE_CATALOG.find(t => t.name === name);
}

/**
 * The configuration a fresh field of this type starts with: every option that declares a default, and
 * nothing else. Options with no default are absent rather than null, so the server falls back to the
 * field type's own default instead of reading an explicit empty.
 */
export function defaultConfiguration(typeName: string): FieldConfigurationDictionary {
  const descriptor = findFieldType(typeName);
  if (!descriptor) {
    return {};
  }

  const configuration: FieldConfigurationDictionary = {};
  for (const option of descriptor.configuration) {
    if (option.default !== undefined) {
      configuration[option.key] = option.default;
    }
  }

  return configuration;
}

/**
 * Whether a field holds a list rather than a scalar — the client-side twin of
 * `VaultExtractFieldTypes.IsMultiValue`. Both branches matter: Tags is always a list, and Select is one
 * only when configured `Multiple`. Testing the type name alone silently mis-renders every multi-Select.
 */
export function isMultiValueField(
  typeName: string | null | undefined,
  configuration: FieldConfigurationDictionary | null | undefined,
): boolean {
  if (typeName === FIELD_TYPES.tags) {
    return true;
  }

  return typeName === FIELD_TYPES.select && configuration?.['Select.Multiple'] === true;
}

/**
 * Whether a field can appear in a field-value filter. False for a type that indexes nothing, and for a
 * field whose searchability the admin turned off — filtering on either is rejected server-side, so the
 * filter UI leaves them out rather than offering a choice that errors.
 */
export function isFilterableField(
  typeName: string | null | undefined,
  isSearchable: boolean | null | undefined,
): boolean {
  return (isSearchable ?? true) && (findFieldType(typeName)?.indexable ?? false);
}

/**
 * Whether a DateTime field holds a date without a time — the distinction v2 carried as two separate data
 * types and v3 carries in configuration. It decides the input control (`date` versus `datetime-local`) and
 * how a stored value is formatted back into one, so both the filter and the detail editor ask here rather
 * than each reading the raw key.
 */
export function isDateOnly(configuration: FieldConfigurationDictionary | null | undefined): boolean {
  // DateTimeInputMode.Date. Month (2) also carries no time, but the browser's month input keeps its own
  // format, so it is not folded in here.
  return configuration?.['DateTime.InputMode'] === 0;
}
