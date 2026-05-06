import { mapEnumToOptions } from '@abp/ng.core';

export enum ChatMessageRole {
  User = 0,
  Assistant = 1,
}

export const chatMessageRoleOptions = mapEnumToOptions(ChatMessageRole);
