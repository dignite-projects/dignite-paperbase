import { EnvironmentProviders } from '@angular/core';
import { provideFlexFieldTypes } from '@dignite/ng.flex-fields';
import { TAGS_FIELD_TYPE } from './tags-field-type';

/**
 * Registers the `Tags` field type. Call alongside `provideFlexFields()`, in the application config:
 *
 * ```ts
 * providers: [provideFlexFields(), provideCKEditorFieldType(), provideTagsFieldType()]
 * ```
 */
export function provideTagsFieldType(): EnvironmentProviders {
  return provideFlexFieldTypes(TAGS_FIELD_TYPE);
}
