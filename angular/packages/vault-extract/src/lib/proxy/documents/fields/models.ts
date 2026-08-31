import type { EntityDto } from '@abp/ng.core';

export interface CreateFieldDefinitionDto {
  documentTypeId: string;
  name: string;
  displayName: string;
  description?: string | null;
  fieldTypeName: string;
  configuration?: Record<string, object> | null;
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
  configuration?: Record<string, object>;
  isRequired?: boolean;
}

export interface FieldDefinitionDto extends EntityDto<string> {
  tenantId?: string | null;
  documentTypeId?: string;
  name?: string;
  displayName?: string;
  description?: string | null;
  fieldTypeName?: string;
  configuration?: Record<string, object>;
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

export interface FieldTypeDto {
  name?: string;
  indexable?: boolean;
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
  configuration?: Record<string, object> | null;
  displayOrder?: number;
  isRequired?: boolean;
  isSearchable?: boolean;
  isUniqueKey?: boolean;
}
