import { RestService, Rest } from '@abp/ng.core';
import type { PagedResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import type { ChatConversationDto, ChatConversationListItemDto, ChatMessageDto, ChatTurnResultDto, CreateChatConversationInput, GetChatConversationListInput, GetChatMessageListInput, SendChatMessageInput } from '../../documents/chat/models';

@Injectable({
  providedIn: 'root',
})
export class DocumentChatService {
  private restService = inject(RestService);
  apiName = 'Default';


  createConversation = (input: CreateChatConversationInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, ChatConversationDto>({
      method: 'POST',
      url: '/api/paperbase/document-chat/conversations',
      body: input,
    },
    { apiName: this.apiName,...config });


  deleteConversation = (conversationId: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'DELETE',
      url: `/api/paperbase/document-chat/conversations/${conversationId}`,
    },
    { apiName: this.apiName,...config });


  getConversation = (conversationId: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, ChatConversationDto>({
      method: 'GET',
      url: `/api/paperbase/document-chat/conversations/${conversationId}`,
    },
    { apiName: this.apiName,...config });


  getConversationList = (input: GetChatConversationListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, PagedResultDto<ChatConversationListItemDto>>({
      method: 'GET',
      url: '/api/paperbase/document-chat/conversations',
      params: { sorting: input.sorting, skipCount: input.skipCount, maxResultCount: input.maxResultCount },
    },
    { apiName: this.apiName,...config });


  getMessageList = (conversationId: string, input: GetChatMessageListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, PagedResultDto<ChatMessageDto>>({
      method: 'GET',
      url: `/api/paperbase/document-chat/conversations/${conversationId}/messages`,
      params: { sorting: input.sorting, skipCount: input.skipCount, maxResultCount: input.maxResultCount },
    },
    { apiName: this.apiName,...config });


  sendMessage = (conversationId: string, input: SendChatMessageInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, ChatTurnResultDto>({
      method: 'POST',
      url: `/api/paperbase/document-chat/conversations/${conversationId}/messages`,
      body: input,
    },
    { apiName: this.apiName,...config });
}
