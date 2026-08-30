import type { FieldConfigurationDictionary } from '../../fields/models';
import type { FieldDataType } from '../../fields/field-data-type.enum';
import type { PackItemAction } from './pack-item-action.enum';
import type { PackImportMode } from './pack-import-mode.enum';

export interface DocumentTypePackDto {
  version?: number;
  typeCode: string;
  displayName: string;
  description?: string | null;
  confidenceThreshold?: number;
  priority?: number;
  fields?: DocumentTypePackFieldDto[];
}

export interface DocumentTypePackFieldDto {
  name: string;
  displayName: string;
  description?: string | null;
  fieldTypeName?: string | null;
  configuration?: FieldConfigurationDictionary | null;
  displayOrder?: number;
  isRequired?: boolean;
  isSearchable?: boolean;
  isUniqueKey?: boolean;

  /**
   * Pack schema version 1 only, and read-only for this client: export always emits version 2, and an
   * imported version-1 pack is upconverted server-side. Kept on the model so a v1 file the operator picks
   * up round-trips through the import call intact instead of losing these fields in transit.
   */
  prompt?: string | null;
  dataType?: FieldDataType;
  allowMultiple?: boolean;
}

export interface DocumentTypePackImportResultDto {
  items?: DocumentTypePackItemResultDto[];
  typesCreated?: number;
  typesUpdated?: number;
  typesSkipped?: number;
  fieldsCreated?: number;
  fieldsUpdated?: number;
  fieldsSkipped?: number;
}

export interface DocumentTypePackItemResultDto {
  typeCode?: string;
  typeAction?: PackItemAction;
  fieldsCreated?: number;
  fieldsUpdated?: number;
  fieldsSkipped?: number;
}

export interface ImportDocumentTypePacksInput {
  packs: DocumentTypePackDto[];
  mode?: PackImportMode;
}
