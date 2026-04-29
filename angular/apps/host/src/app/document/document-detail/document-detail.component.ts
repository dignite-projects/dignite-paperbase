import {
  Component,
  OnInit,
  OnDestroy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { LocalizationPipe } from '@abp/ng.core';
import { interval, Subscription, switchMap, startWith } from 'rxjs';
import { DocumentService } from '../../proxy/document.service';
import { DocumentDto, DocumentLifecycleStatus, DocumentReviewStatus } from '../../proxy/models';
import { DocumentQaPanelComponent } from '../document-qa-panel/document-qa-panel.component';
import { DocumentRelationsComponent } from '../document-relations/document-relations.component';

@Component({
  selector: 'app-document-detail',
  templateUrl: './document-detail.component.html',
  styleUrls: ['./document-detail.component.scss'],
  imports: [CommonModule, RouterModule, LocalizationPipe, DocumentQaPanelComponent, DocumentRelationsComponent],
})
export class DocumentDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly documentService = inject(DocumentService);

  document = signal<DocumentDto | null>(null);
  isLoading = signal(true);
  isTextExpanded = signal(false);
  imageError = signal(false);
  activeTab = signal<'info' | 'qa' | 'relations'>('info');

  readonly DocumentLifecycleStatus = DocumentLifecycleStatus;
  readonly DocumentReviewStatus = DocumentReviewStatus;

  needsReview = computed(() =>
    this.document()?.reviewStatus === DocumentReviewStatus.PendingReview
  );

  isProcessing = computed(() => {
    const status = this.document()?.lifecycleStatus;
    return status === DocumentLifecycleStatus.Uploaded ||
           status === DocumentLifecycleStatus.Processing;
  });

  isReady = computed(() =>
    this.document()?.lifecycleStatus === DocumentLifecycleStatus.Ready
  );

  isImage = computed(() =>
    this.document()?.fileOrigin?.contentType?.startsWith('image/') ?? false
  );

  private documentId!: string;
  private pollSubscription?: Subscription;

  ngOnInit(): void {
    this.documentId = this.route.snapshot.paramMap.get('id')!;
    this.startPolling();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  private startPolling(): void {
    this.pollSubscription = interval(3000)
      .pipe(
        startWith(0),
        switchMap(() => this.documentService.get(this.documentId))
      )
      .subscribe({
        next: doc => {
          this.isLoading.set(false);
          this.document.set(doc);

          if (doc.lifecycleStatus === DocumentLifecycleStatus.Ready ||
              doc.lifecycleStatus === DocumentLifecycleStatus.Failed) {
            this.stopPolling();
          }
        },
        error: () => {
          this.isLoading.set(false);
          this.stopPolling();
        },
      });
  }

  private stopPolling(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = undefined;
  }

  setTab(tab: 'info' | 'qa' | 'relations'): void {
    this.activeTab.set(tab);
  }

  getImageUrl(): string {
    return this.documentService.getBlobUrl(this.documentId);
  }

  onImageError(): void {
    this.imageError.set(true);
  }

  toggleText(): void {
    this.isTextExpanded.set(!this.isTextExpanded());
  }

  goBack(): void {
    this.router.navigate(['/documents']);
  }

  getStatusBadgeClass(status: DocumentLifecycleStatus): string {
    switch (status) {
      case DocumentLifecycleStatus.Uploaded:   return 'badge bg-secondary';
      case DocumentLifecycleStatus.Processing: return 'badge bg-warning text-dark';
      case DocumentLifecycleStatus.Ready:      return 'badge bg-success';
      case DocumentLifecycleStatus.Failed:     return 'badge bg-danger';
      default:                                 return 'badge bg-secondary';
    }
  }

  getStatusLabel(status: DocumentLifecycleStatus): string {
    switch (status) {
      case DocumentLifecycleStatus.Uploaded:   return '::Document:Status:Uploaded';
      case DocumentLifecycleStatus.Processing: return '::Document:Status:Processing';
      case DocumentLifecycleStatus.Ready:      return '::Document:Status:Ready';
      case DocumentLifecycleStatus.Failed:     return '::Document:Status:Failed';
      default:                                 return '::Document:Status:Unknown';
    }
  }
}
