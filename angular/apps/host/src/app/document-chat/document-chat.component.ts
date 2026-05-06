import { CommonModule } from '@angular/common';
import {
  AfterViewChecked,
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SafeHtml, DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { LocalizationPipe, LocalizationService } from '@abp/ng.core';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import { finalize } from 'rxjs';
import { marked } from 'marked';
import { DocumentService } from '../proxy/document.service';
import { ChatMessageRole } from '../proxy/documents/chat/chat-message-role.enum';
import {
  ChatCitationDto,
  ChatConversationDto,
  ChatConversationListItemDto,
  ChatMessageDto,
  CreateChatConversationInput,
} from '../proxy/documents/chat/models';
import { DocumentDto } from '../proxy/models';
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

interface SelectedCitation {
  messageId: string;
  citationIndex: number;
  citation: ChatCitationDto;
}

@Component({
  selector: 'app-document-chat',
  templateUrl: './document-chat.component.html',
  styleUrls: ['./document-chat.component.scss'],
  imports: [CommonModule, FormsModule, RouterModule, LocalizationPipe],
})
export class DocumentChatComponent implements OnInit, AfterViewChecked {
  private readonly chatService = inject(DocumentChatService);
  private readonly documentService = inject(DocumentService);
  private readonly route = inject(ActivatedRoute);
  private readonly confirmation = inject(ConfirmationService);
  private readonly toaster = inject(ToasterService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly localization = inject(LocalizationService);

  readonly ChatMessageRole = ChatMessageRole;

  conversations = signal<ChatConversationListItemDto[]>([]);
  activeConversation = signal<ChatConversationDto | null>(null);
  messages = signal<ChatMessageView[]>([]);
  sourceDocument = signal<DocumentDto | null>(null);
  selectedCitation = signal<SelectedCitation | null>(null);

  title = signal('');
  documentId = signal('');
  documentTypeCode = signal('');
  message = signal('');

  isLoadingConversations = signal(false);
  isLoadingMessages = signal(false);
  isCreating = signal(false);
  isSending = signal(false);
  isLoadingSource = signal(false);
  sourceError = signal<string | null>(null);

  activeConversationId = computed(() => this.activeConversation()?.id ?? null);
  canSend = computed(() => !!this.message().trim() && !this.isSending());
  // Markdown is rendered (rather than displayed as `<pre>` text) because the project
  // is AI-first and the persisted Markdown is the single canonical text artifact —
  // headings/lists/tables carry semantic structure that helps the user scan.
  // marked is configured with `mangle:false` and `headerIds:false` to keep output
  // deterministic and free of injected anchor IDs.
  markdownHtml = computed<SafeHtml | null>(() => {
    const markdown = this.sourceDocument()?.markdown;
    if (!markdown) return null;
    const html = marked.parse(markdown, { async: false, gfm: true }) as string;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  });
  // The snippet may not exist in the rendered DOM (LLM rephrased the chunk, OCR
  // changed between embedding and view). The signal is recomputed every time the
  // citation changes; the actual `<mark>` wrapping happens after view-check via
  // tryHighlightSnippet().
  snippetMissing = signal(false);
  private readonly markdownContainer = viewChild<ElementRef<HTMLElement>>('markdownContainer');
  private lastHighlightedSnippet: string | null = null;
  private lastHighlightedDocumentId: string | null = null;
  private sourceRequestId = 0;

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

  ngAfterViewChecked(): void {
    this.tryHighlightSnippet();
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
        this.selectedCitation.set(null);
        this.sourceError.set(null);
        if (conversation.documentId) {
          this.loadSourceDocument(conversation.documentId);
        } else {
          this.sourceRequestId++;
          this.isLoadingSource.set(false);
          this.sourceDocument.set(null);
        }
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
    if (conversation.documentId) {
      return this.localization.instant({
        key: '::DocumentChat:Scope:Document',
        defaultValue: `doc:${conversation.documentId.slice(0, 8)}`,
      }, conversation.documentId.slice(0, 8));
    }
    return this.localization.instant({ key: '::DocumentChat:Scope:Global', defaultValue: 'global' });
  }

  selectCitation(messageId: string, citationIndex: number, citation: ChatCitationDto): void {
    this.selectedCitation.set({ messageId, citationIndex, citation });

    if (!citation.documentId) {
      this.sourceRequestId++;
      this.isLoadingSource.set(false);
      this.sourceDocument.set(null);
      this.sourceError.set('::DocumentChat:SourceMissingDocumentId');
      return;
    }

    this.loadSourceDocument(citation.documentId);
  }

  isSelectedCitation(messageId: string, citationIndex: number): boolean {
    const selected = this.selectedCitation();
    return selected?.messageId === messageId && selected.citationIndex === citationIndex;
  }

  citationKey(messageId: string, citationIndex: number): string {
    return `${messageId}::${citationIndex}`;
  }

  sourceTitle(document: DocumentDto): string {
    return document.fileOrigin?.originalFileName || document.originalFileBlobName || document.id || 'Source document';
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
          this.selectedCitation.set(null);
          if (conversation.documentId) {
            this.loadSourceDocument(conversation.documentId);
          } else {
            this.sourceRequestId++;
            this.isLoadingSource.set(false);
            this.sourceDocument.set(null);
          }
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

  /**
   * Wraps the first occurrence of the selected citation's snippet inside the
   * rendered Markdown container in a `<mark>` tag. Runs after every view-check
   * because the markdown HTML and the selected citation can update independently.
   *
   * Trade-offs:
   *  - First-occurrence only. If the snippet appears in multiple chunks, the UI
   *    cannot disambiguate without persisted chunk offsets (intentionally not
   *    introduced — see docs/document-chat.md citation-to-source navigation).
   *  - Snippet must match raw Markdown text from the rendered DOM. The chunker
   *    typically preserves whitespace/punctuation, so direct substring search
   *    works in practice. Falls back to `snippetMissing=true` (warning banner)
   *    when no match is found.
   */
  private tryHighlightSnippet(): void {
    const container = this.markdownContainer()?.nativeElement;
    if (!container) {
      this.lastHighlightedSnippet = null;
      this.lastHighlightedDocumentId = null;
      return;
    }

    const document = this.sourceDocument();
    const snippet = this.selectedCitation()?.citation.snippet?.trim();
    const documentId = document?.id ?? null;

    if (!snippet || !document?.markdown) {
      if (this.lastHighlightedSnippet) {
        this.removeExistingHighlight(container);
        this.lastHighlightedSnippet = null;
        this.lastHighlightedDocumentId = null;
      }
      this.snippetMissing.set(false);
      return;
    }

    if (snippet === this.lastHighlightedSnippet && documentId === this.lastHighlightedDocumentId) {
      return;
    }

    this.removeExistingHighlight(container);

    const matched = this.wrapFirstOccurrence(container, snippet);
    this.snippetMissing.set(!matched);
    this.lastHighlightedSnippet = snippet;
    this.lastHighlightedDocumentId = documentId;

    if (matched) {
      const mark = container.querySelector('mark.chat-citation-mark') as HTMLElement | null;
      mark?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }

  private removeExistingHighlight(container: HTMLElement): void {
    const marks = container.querySelectorAll('mark.chat-citation-mark');
    marks.forEach(mark => {
      const parent = mark.parentNode;
      if (!parent) return;
      while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
      parent.removeChild(mark);
      parent.normalize();
    });
  }

  private wrapFirstOccurrence(container: HTMLElement, snippet: string): boolean {
    const walker = (container.ownerDocument ?? document).createTreeWalker(
      container,
      NodeFilter.SHOW_TEXT
    );
    let node: Node | null;
    while ((node = walker.nextNode())) {
      const text = node.nodeValue ?? '';
      const idx = text.indexOf(snippet);
      if (idx < 0) continue;

      const range = (container.ownerDocument ?? document).createRange();
      range.setStart(node, idx);
      range.setEnd(node, idx + snippet.length);
      const mark = (container.ownerDocument ?? document).createElement('mark');
      mark.className = 'chat-citation-mark';
      mark.setAttribute('role', 'button');
      mark.setAttribute('tabindex', '0');
      mark.title = '跳到引用';
      // Bidirectional navigation: clicking the source-pane highlight scrolls the
      // matching chat-citation button into view. The citation is already selected
      // (that is what created the highlight), so we only need the scroll.
      const handler = () => this.scrollSelectedCitationIntoView();
      mark.addEventListener('click', handler);
      mark.addEventListener('keydown', (event: Event) => {
        const keyEvent = event as KeyboardEvent;
        if (keyEvent.key === 'Enter' || keyEvent.key === ' ') {
          event.preventDefault();
          handler();
        }
      });
      range.surroundContents(mark);
      return true;
    }
    return false;
  }

  private scrollSelectedCitationIntoView(): void {
    const selected = this.selectedCitation();
    if (!selected) return;
    const key = this.citationKey(selected.messageId, selected.citationIndex);
    const button = document.querySelector(
      `button.citation[data-citation-key="${CSS.escape(key)}"]`
    ) as HTMLElement | null;
    button?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  private loadSourceDocument(documentId: string): void {
    const requestId = ++this.sourceRequestId;
    this.isLoadingSource.set(true);
    this.sourceError.set(null);

    this.documentService
      .get(documentId)
      .pipe(finalize(() => {
        if (requestId === this.sourceRequestId) {
          this.isLoadingSource.set(false);
        }
      }))
      .subscribe({
        next: document => {
          if (requestId !== this.sourceRequestId) return;
          this.sourceDocument.set(document);
        },
        error: () => {
          if (requestId !== this.sourceRequestId) return;
          this.sourceDocument.set(null);
          this.sourceError.set('::DocumentChat:SourceLoadFailed');
        },
      });
  }
}
