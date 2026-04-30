import { mapEnumToOptions } from '@abp/ng.core';

export enum SourceType {
  Physical = 1,
  Digital = 2,
}

export const sourceTypeOptions = mapEnumToOptions(SourceType);
