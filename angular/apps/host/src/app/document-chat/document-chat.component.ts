import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { LocalizationPipe } from '@abp/ng.core';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import { finalize } from 'rxjs';
import { ChatMessageRole } from '../proxy/documents/chat/chat-message-role.enum';
import {
  ChatCitationDto,
  ChatConversationDto,
  ChatConversationListItemDto,
  ChatMessageDto,
  CreateChatConversationInput,
} from '../proxy/documents/chat/models';
import { DocumentChatService } from '../proxy/http-api/documents/document-chat.service';

interface ChatMessageView {
  id: string;
  role: ChatMessageRole;
  content: string;
  creationTime?: string;
  citations: ChatCitationDto[];
  isDegraded?: boolean;
  isPending?: boolean;
  isError?: boolean;
}

@Component({
  selector: 'app-document-chat',
  templateUrl: './document-chat.component.html',
  styleUrls: ['./document-chat.component.scss'],
  imports: [CommonModule, FormsModule, RouterModule, LocalizationPipe],
})
export class DocumentChatComponent implements OnInit {
  private readonly chatService = inject(DocumentChatService);
  private readonly route = inject(ActivatedRoute);
  private readonly confirmation = inject(ConfirmationService);
  private readonly toaster = inject(ToasterService);

  readonly ChatMessageRole = ChatMessageRole;

  conversations = signal<ChatConversationListItemDto[]>([]);
  activeConversation = signal<ChatConversationDto | null>(null);
  messages = signal<ChatMessageView[]>([]);

  title = signal('');
  documentId = signal('');
  documentTypeCode = signal('');
  message = signal('');

  isLoadingConversations = signal(false);
  isLoadingMessages = signal(false);
  isCreating = signal(false);
  isSending = signal(false);

  activeConversationId = computed(() => this.activeConversation()?.id ?? null);
  canSend = computed(() => !!this.message().trim() && !this.isSending());

  ngOnInit(): void {
    const query = this.route.snapshot.queryParamMap;
    const documentId = query.get('documentId');
    const documentTypeCode = query.get('documentTypeCode');
    const title = query.get('title');

    if (documentId) this.documentId.set(documentId);
    if (documentTypeCode) this.documentTypeCode.set(documentTypeCode);
    if (title) this.title.set(title);

    this.loadConversations(() => {
      if (documentId || documentTypeCode) {
        this.openOrCreateScopedConversation(documentId, documentTypeCode, title);
      }
    });
  }

  loadConversations(afterLoad?: () => void): void {
    this.isLoadingConversations.set(true);
    this.chatService
      .getConversationList({
        maxResultCount: 50,
        skipCount: 0,
        sorting: 'CreationTime DESC',
      })
      .pipe(finalize(() => this.isLoadingConversations.set(false)))
      .subscribe({
        next: result => {
          this.conversations.set(result.items ?? []);
          afterLoad?.();
        },
        error: () => this.toaster.error('::DocumentChat:LoadFailed', '::Error'),
      });
  }

  selectConversation(conversationId?: string): void {
    if (!conversationId) return;

    this.isLoadingMessages.set(true);
    this.chatService.getConversation(conversationId).subscribe({
      next: conversation => {
        this.activeConversation.set(conversation);
        this.chatService
          .getMessageList(conversationId, {
            maxResultCount: 100,
            skipCount: 0,
            sorting: 'CreationTime ASC',
          })
          .pipe(finalize(() => this.isLoadingMessages.set(false)))
          .subscribe({
            next: result => this.messages.set((result.items ?? []).map(m => this.toMessageView(m))),
            error: () => this.toaster.error('::DocumentChat:LoadFailed', '::Error'),
          });
      },
      error: () => {
        this.isLoadingMessages.set(false);
        this.toaster.error('::DocumentChat:LoadFailed', '::Error');
      },
    });
  }

  createConversation(): void {
    this.createAndOpen({
      title: this.emptyToNull(this.title()),
      documentId: this.emptyToNull(this.documentId()),
      documentTypeCode: this.emptyToNull(this.documentTypeCode()),
    });
  }

  deleteConversation(conversation: ChatConversationListItemDto, event: Event): void {
    event.stopPropagation();
    if (!conversation.id) return;

    this.confirmation
      .warn('::DocumentChat:AreYouSureToDelete', '::AreYouSure')
      .subscribe(status => {
        if (status !== Confirmation.Status.confirm) return;

        this.chatService.deleteConversation(conversation.id!).subscribe({
          next: () => {
            if (this.activeConversationId() === conversation.id) {
              this.activeConversation.set(null);
              this.messages.set([]);
            }
            this.loadConversations();
          },
          error: () => this.toaster.error('::DocumentChat:DeleteFailed', '::Error'),
        });
      });
  }

  sendMessage(): void {
    const text = this.message().trim();
    if (!text || this.isSending()) return;

    const active = this.activeConversation();
    if (!active?.id) {
      this.createAndOpen({}, created => this.sendToConversation(created, text));
      return;
    }

    this.sendToConversation(active, text);
  }

  onMessageKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      this.sendMessage();
    }
  }

  sourceLabel(conversation: ChatConversationDto | ChatConversationListItemDto): string {
    if (conversation.documentTypeCode) return conversation.documentTypeCode;
    if (conversation.documentId) return `doc:${conversation.documentId.slice(0, 8)}`;
    return 'global';
  }

  private openOrCreateScopedConversation(
    documentId: string | null,
    documentTypeCode: string | null,
    title: string | null
  ): void {
    const existing = this.conversations().find(c =>
      (documentId && c.documentId === documentId) ||
      (!documentId && documentTypeCode && c.documentTypeCode === documentTypeCode)
    );

    if (existing?.id) {
      this.selectConversation(existing.id);
      return;
    }

    this.createAndOpen({
      title: title || null,
      documentId,
      documentTypeCode,
    });
  }

  private createAndOpen(
    input: CreateChatConversationInput,
    afterCreate?: (conversation: ChatConversationDto) => void
  ): void {
    this.isCreating.set(true);
    this.chatService
      .createConversation(input)
      .pipe(finalize(() => this.isCreating.set(false)))
      .subscribe({
        next: conversation => {
          this.activeConversation.set(conversation);
          this.messages.set([]);
          this.title.set('');
          if (!input.documentId) this.documentId.set('');
          if (!input.documentTypeCode) this.documentTypeCode.set('');
          this.loadConversations();
          afterCreate?.(conversation);
        },
        error: () => this.toaster.error('::DocumentChat:CreateFailed', '::Error'),
      });
  }

  private sendToConversation(conversation: ChatConversationDto, text: string): void {
    if (!conversation.id) return;

    const clientTurnId = crypto.randomUUID();
    const pendingAssistantId = `pending-${clientTurnId}`;
    this.message.set('');
    this.messages.update(items => [
      ...items,
      {
        id: clientTurnId,
        role: ChatMessageRole.User,
        content: text,
        citations: [],
      },
      {
        id: pendingAssistantId,
        role: ChatMessageRole.Assistant,
        content: '',
        citations: [],
        isPending: true,
      },
    ]);

    this.isSending.set(true);
    this.chatService
      .sendMessage(conversation.id, { message: text, clientTurnId })
      .pipe(finalize(() => this.isSending.set(false)))
      .subscribe({
        next: result => {
          this.messages.update(items =>
            items.map(item =>
              item.id === pendingAssistantId
                ? {
                    id: result.assistantMessageId ?? pendingAssistantId,
                    role: ChatMessageRole.Assistant,
                    content: result.answer ?? '',
                    citations: result.citations ?? [],
                    isDegraded: result.isDegraded,
                  }
                : item
            )
          );
          this.loadConversations();
        },
        error: () => {
          this.messages.update(items =>
            items.map(item =>
              item.id === pendingAssistantId
                ? {
                    ...item,
                    content: 'DocumentChat:SendFailed',
                    isPending: false,
                    isError: true,
                  }
                : item
            )
          );
          this.toaster.error('::DocumentChat:SendFailed', '::Error');
        },
      });
  }

  private toMessageView(message: ChatMessageDto): ChatMessageView {
    return {
      id: message.id ?? crypto.randomUUID(),
      role: message.role ?? ChatMessageRole.Assistant,
      content: message.content ?? '',
      creationTime: message.creationTime,
      citations: this.parseCitations(message.citationsJson),
    };
  }

  private parseCitations(json?: string | null): ChatCitationDto[] {
    if (!json) return [];
    try {
      const parsed = JSON.parse(json);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  private emptyToNull(value: string): string | null {
    const trimmed = value.trim();
    return trimmed ? trimmed : null;
  }
}
