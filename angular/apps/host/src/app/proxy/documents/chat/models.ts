import type { EntityDto, FullAuditedEntityDto, PagedAndSortedResultRequestDto } from '@abp/ng.core';
import type { ChatMessageRole } from './chat-message-role.enum';

export interface ChatCitationDto {
  documentId?: string;
  pageNumber?: number | null;
  chunkIndex?: number | null;
  snippet?: string;
  sourceName?: string;
}

export interface ChatConversationDto extends FullAuditedEntityDto<string> {
  tenantId?: string | null;
  title?: string;
  documentId?: string | null;
  documentTypeCode?: string | null;
  topK?: number | null;
  minScore?: number | null;
}

export interface ChatConversationListItemDto extends EntityDto<string> {
  title?: string;
  documentId?: string | null;
  documentTypeCode?: string | null;
  creationTime?: string;
}

export interface ChatMessageDto extends EntityDto<string> {
  conversationId?: string;
  role?: ChatMessageRole;
  content?: string;
  citationsJson?: string | null;
  clientTurnId?: string | null;
  creationTime?: string;
}

export interface ChatTurnResultDto {
  userMessageId?: string;
  assistantMessageId?: string;
  answer?: string;
  citations?: ChatCitationDto[];
  isDegraded?: boolean;
}

export interface CreateChatConversationInput {
  title?: string | null;
  documentId?: string | null;
  documentTypeCode?: string | null;
  topK?: number | null;
  minScore?: number | null;
}

export interface GetChatConversationListInput extends PagedAndSortedResultRequestDto {
}

export interface GetChatMessageListInput extends PagedAndSortedResultRequestDto {
}

export interface SendChatMessageInput {
  message: string;
  clientTurnId: string;
}
