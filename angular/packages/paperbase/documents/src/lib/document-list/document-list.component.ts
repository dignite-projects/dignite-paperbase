import {
  Component,
  OnInit,
  OnDestroy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe } from '@abp/ng.core';
import type { PagedResultDto } from '@abp/ng.core';
import { ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import { Confirmation } from '@abp/ng.theme.shared';
import { Subscription, interval, switchMap, startWith } from 'rxjs';
import {
  ClassificationCandidate,
  DocumentDto,
  DocumentLifecycleStatus,
  DocumentPipelineRunDto,
  DocumentReviewStatus,
  DocumentService,
  GetDocumentListInput,
} from '@dignite/paperbase';

interface UploadResult {
  fileName: string;
  documentId?: string;
  succeeded: boolean;
  errorMessage?: string;
}

@Component({
  selector: 'lib-document-list',
  templateUrl: './document-list.component.html',
  styleUrls: ['./document-list.component.scss'],
  imports: [CommonModule, RouterModule, FormsModule, LocalizationPipe],
})
export class DocumentListComponent implements OnInit, OnDestroy {
  private readonly documentService = inject(DocumentService);
  private readonly router = inject(Router);
  private readonly confirmation = inject(ConfirmationService);
  private readonly toaster = inject(ToasterService);

  documents = signal<PagedResultDto<DocumentDto>>({ totalCount: 0, items: [] });
  isLoading = signal(true);
  isExporting = signal(false);
  isBulkUploading = signal(false);
  bulkUploadResults = signal<UploadResult[]>([]);

  reviewStatusFilter = signal<DocumentReviewStatus | undefined>(undefined);
  confirmingDoc = signal<DocumentDto | null>(null);
  selectedTypeCode = signal('');
  isConfirming = signal(false);

  page = signal(0);
  pageSize = 10;
  totalPages = computed(() => Math.ceil(this.documents().totalCount / this.pageSize));
  pendingReviewCount = computed(() =>
    this.documents().items.filter(d => d.reviewStatus === DocumentReviewStatus.PendingReview).length
  );

  private activeFilter: GetDocumentListInput = {};

  readonly DocumentLifecycleStatus = DocumentLifecycleStatus;
  readonly DocumentReviewStatus = DocumentReviewStatus;

  private pollSubscription?: Subscription;

  ngOnInit(): void {
    this.startPolling();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  private startPolling(): void {
    this.pollSubscription = interval(3000)
      .pipe(
        startWith(0),
        switchMap(() => this.documentService.getList({
          ...this.activeFilter,
          maxResultCount: this.pageSize,
          skipCount: this.page() * this.pageSize,
          sorting: 'creationTime desc',
          reviewStatus: this.reviewStatusFilter(),
        }))
      )
      .subscribe({
        next: result => {
          this.isLoading.set(false);
          this.documents.set(result);

          const hasProcessing = result.items.some(
            d => d.lifecycleStatus === DocumentLifecycleStatus.Processing ||
                 d.lifecycleStatus === DocumentLifecycleStatus.Uploaded
          );
          if (!hasProcessing) {
            this.stopPolling();
          }
        },
        error: () => {
          this.isLoading.set(false);
        },
      });
  }

  private stopPolling(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = undefined;
  }

  navigateTo(page: number): void {
    this.page.set(page);
    this.stopPolling();
    this.isLoading.set(true);
    this.startPolling();
  }

  openDetail(doc: DocumentDto): void {
    this.router.navigate(['/documents', doc.id]);
  }

  uploadNew(): void {
    this.router.navigate(['/documents/upload']);
  }

  onBulkFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    const files = Array.from(input.files);

    this.isBulkUploading.set(true);
    this.bulkUploadResults.set([]);

    let completed = 0;
    const onAllDone = () => {
      this.isBulkUploading.set(false);
      this.stopPolling();
      this.isLoading.set(true);
      this.startPolling();
      input.value = '';
    };

    for (const file of files) {
      this.documentService.upload(file).subscribe({
        next: doc => {
          this.bulkUploadResults.update(r => [...r, { fileName: file.name, documentId: doc.id, succeeded: true }]);
          if (++completed === files.length) onAllDone();
        },
        error: err => {
          this.bulkUploadResults.update(r => [...r, { fileName: file.name, succeeded: false, errorMessage: err.message }]);
          if (++completed === files.length) onAllDone();
        },
      });
    }
  }

  exportCsv(): void {
    const url = this.documentService.getExportUrl(this.activeFilter);
    window.open(url, '_blank');
  }

  delete(doc: DocumentDto, event: Event): void {
    event.stopPropagation();
    this.confirmation
      .warn('::Document:AreYouSureToDelete', '::AreYouSure')
      .subscribe(status => {
        if (status === Confirmation.Status.confirm) {
          this.documentService.delete(doc.id).subscribe({
            next: () => {
              this.toaster.success('::Document:DeletedSuccessfully', '::Success');
              this.stopPolling();
              this.isLoading.set(true);
              this.startPolling();
            },
          });
        }
      });
  }

  toggleManualReviewFilter(): void {
    this.reviewStatusFilter.update(v =>
      v === DocumentReviewStatus.PendingReview ? undefined : DocumentReviewStatus.PendingReview
    );
    this.page.set(0);
    this.stopPolling();
    this.isLoading.set(true);
    this.startPolling();
  }

  getLatestClassificationRun(doc: DocumentDto): DocumentPipelineRunDto | null {
    const runs = doc.pipelineRuns?.filter(r => r.pipelineCode === 'classification') ?? [];
    if (runs.length === 0) return null;
    return runs.reduce((prev, curr) => curr.attemptNumber > prev.attemptNumber ? curr : prev);
  }

  needsConfirmation(doc: DocumentDto): boolean {
    return doc.reviewStatus === DocumentReviewStatus.PendingReview;
  }

  getCandidates(doc: DocumentDto): ClassificationCandidate[] {
    const run = this.getLatestClassificationRun(doc);
    const fromRun = run?.extraProperties?.['Candidates'] as ClassificationCandidate[] | undefined;
    if (fromRun && fromRun.length > 0) return fromRun;
    return doc.documentTypeCode ? [{ typeCode: doc.documentTypeCode, confidenceScore: 1 }] : [];
  }

  openConfirmDialog(doc: DocumentDto, event: Event): void {
    event.stopPropagation();
    this.confirmingDoc.set(doc);
    const candidates = this.getCandidates(doc);
    const defaultCode = candidates[0]?.typeCode ?? doc.documentTypeCode ?? '';
    this.selectedTypeCode.set(defaultCode);
  }

  closeConfirmDialog(): void {
    this.confirmingDoc.set(null);
    this.selectedTypeCode.set('');
  }

  onTypeCodeInput(event: Event): void {
    this.selectedTypeCode.set((event.target as HTMLInputElement).value);
  }

  submitConfirmation(): void {
    const doc = this.confirmingDoc();
    if (!doc || !this.selectedTypeCode()) return;
    this.isConfirming.set(true);
    this.documentService.confirmClassification(doc.id, this.selectedTypeCode()).subscribe({
      next: () => {
        this.isConfirming.set(false);
        this.closeConfirmDialog();
        this.toaster.success('::Document:ClassificationConfirmed', '::Success');
        this.stopPolling();
        this.isLoading.set(true);
        this.startPolling();
      },
      error: () => {
        this.isConfirming.set(false);
        this.toaster.error('::Document:ConfirmFailed', '::Error');
      },
    });
  }

  getStatusBadgeClass(status: DocumentLifecycleStatus): string {
    switch (status) {
      case DocumentLifecycleStatus.Uploaded:
        return 'badge bg-secondary';
      case DocumentLifecycleStatus.Processing:
        return 'badge bg-warning text-dark';
      case DocumentLifecycleStatus.Ready:
        return 'badge bg-success';
      case DocumentLifecycleStatus.Failed:
        return 'badge bg-danger';
      default:
        return 'badge bg-secondary';
    }
  }

  getStatusLabel(status: DocumentLifecycleStatus): string {
    switch (status) {
      case DocumentLifecycleStatus.Uploaded:
        return '::Document:Status:Uploaded';
      case DocumentLifecycleStatus.Processing:
        return '::Document:Status:Processing';
      case DocumentLifecycleStatus.Ready:
        return '::Document:Status:Ready';
      case DocumentLifecycleStatus.Failed:
        return '::Document:Status:Failed';
      default:
        return '::Document:Status:Unknown';
    }
  }

  isImage(doc: DocumentDto): boolean {
    return doc.fileOrigin?.contentType?.startsWith('image/') ?? false;
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
