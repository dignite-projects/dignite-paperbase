import type { FieldTypeDefinition } from '@dignite/ng.flex-fields';
import { TagsConfigComponent } from './tags-config.component';
import { TagsControlComponent } from './tags-control.component';
import { TagsSearchComponent } from './tags-search.component';
import { TagsViewComponent } from './tags-view.component';

/**
 * The `Tags` field type: Vault Extract's own open-vocabulary multi-value type
 * (`Dignite.Vault.Extract.FlexFields.Tags.TagsFieldType` on the server), the complement of the
 * kernel's closed-vocabulary `Select`.
 */
export const TAGS_FIELD_TYPE: FieldTypeDefinition = {
  name: 'Tags',
  displayNameKey: 'VaultExtractFlexFields::FieldType:Tags',
  configComponent: TagsConfigComponent,
  controlComponent: TagsControlComponent,
  viewComponent: TagsViewComponent,
  searchComponent: TagsSearchComponent,
};
