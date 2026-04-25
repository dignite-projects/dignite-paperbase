import { mapEnumToOptions } from '@abp/ng.core';

export enum RelationSource {
  Manual = 1,
  AiSuggested = 2,
  ModuleAuto = 3,
}

export const relationSourceOptions = mapEnumToOptions(RelationSource);
