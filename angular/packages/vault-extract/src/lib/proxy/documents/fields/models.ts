import type { EntityDto } from '@abp/ng.core';

/**
 * Type-specific field configuration, keyed by the field type's own namespaced constants
 * ("Text.CharLimit", "Select.Options", "DateTime.InputMode", ...). Opaque on the wire on purpose: the
 * field type owns the shape, so the client reads and writes it through the per-type helpers in
 * `field-type-configuration` rather than by reaching for keys directly.
 */
export type FieldConfigurationDictionary = Record<string, unknown>;

export interface CreateFieldDefinitionDto {
  documentTypeId: string;
  name: string;
  displayName: string;
  description?: string | null;
  fieldTypeName: string;
  configuration?: FieldConfigurationDictionary | null;
  displayOrder?: number;
  isRequired?: boolean;
  isSearchable?: boolean;
  isUniqueKey?: boolean;
}

export interface DraftFieldDefinitionInput {
  prompt: string;
  forNewField?: boolean;
}

export interface FieldDefinitionDraftDto {
  displayName?: string;
  name?: string;
  fieldTypeName?: string;
  configuration?: FieldConfigurationDictionary;
  isRequired?: boolean;
}

export interface FieldDefinitionDto extends EntityDto<string> {
  tenantId?: string | null;
  documentTypeId?: string;
  name?: string;
  displayName?: string;
  description?: string | null;
  fieldTypeName?: string;
  configuration?: FieldConfigurationDictionary;
  displayOrder?: number;
  isRequired?: boolean;
  isSearchable?: boolean;
  isUniqueKey?: boolean;
}

export interface FieldPromptPolishInput {
  prompt: string;
}

export interface FieldPromptPolishResultDto {
  prompt?: string;
}

export interface GetFieldDefinitionListInput {
  documentTypeId?: string | null;
  onlyDeleted?: boolean;
}

export interface UpdateFieldDefinitionDto {
  name: string;
  displayName: string;
  description?: string | null;
  fieldTypeName: string;
  configuration?: FieldConfigurationDictionary | null;
  displayOrder?: number;
  isRequired?: boolean;
  isSearchable?: boolean;
  isUniqueKey?: boolean;
}
