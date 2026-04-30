import { mapEnumToOptions } from '@abp/ng.core';

export enum QaMode {
  Auto = 0,
  Rag = 1,
  FullText = 2,
}

export const qaModeOptions = mapEnumToOptions(QaMode);
