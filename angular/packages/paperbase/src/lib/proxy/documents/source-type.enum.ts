import { mapEnumToOptions } from '@abp/ng.core';

export enum SourceType {
  Physical = 0,
  Digital = 1,
}

export const sourceTypeOptions = mapEnumToOptions(SourceType);
