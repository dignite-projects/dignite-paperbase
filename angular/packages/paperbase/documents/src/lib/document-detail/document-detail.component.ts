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
import { LocalizationPipe, PermissionService } from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { interval, Subscription, switchMap, startWith } from 'rxjs';
import {
  DOCUMENT_TYPE_HANDLER,
  DocumentDto,
  DocumentLifecycleStatus,
  DocumentPipelineRunDto,
  DocumentReviewStatus,
  DocumentService,
  PipelineRunStatus,
} from '@dignite/paperbase';
import { DocumentRelationsComponent } from '../document-relations/document-relations.component';

interface PipelineRow {
  pipelineCode: string;
  labelKey: string;
  isKnown: boolean;
  run: DocumentPipelineRunDto | null;
}

const KNOWN_PIPELINE_CODES = ['text-extraction', 'classification', 'embedding'] as const;

@Component({
  selector: 'lib-document-detail',
  templateUrl: './document-detail.component.html',
  styleUrls: ['./document-detail.component.scss'],
  imports: [CommonModule, RouterModule, LocalizationPipe, DocumentRelationsComponent],
})
export class DocumentDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly documentService = inject(DocumentService);
  private readonly toaster = inject(ToasterService);
  private readonly permissionService = inject(PermissionService);
  private readonly typeHandlers = inject(DOCUMENT_TYPE_HANDLER, { optional: true }) ?? [];

  document = signal<DocumentDto | null>(null);
  isLoading = signal(true);
  isTextExpanded = signal(false);
  imageError = signal(false);
  activeTab = signal<'info' | 'relations'>('info');
  retryingPipeline = signal<string | null>(null);

  readonly DocumentLifecycleStatus = DocumentLifecycleStatus;
  readonly DocumentReviewStatus = DocumentReviewStatus;
  readonly PipelineRunStatus = PipelineRunStatus;

  pipelineRows = computed<PipelineRow[]>(() => {
    const doc = this.document();
    if (!doc) return [];

    const allRuns = doc.pipelineRuns ?? [];
    const known: PipelineRow[] = KNOWN_PIPELINE_CODES.map(code => ({
      pipelineCode: code,
      labelKey: `::Document:Pipeline:${code}`,
      isKnown: true,
      run: this.pickLatestRun(allRuns, code),
    }));

    const unknownCodes = Array.from(
      new Set(
        allRuns
          .map(r => r.pipelineCode)
          .filter(code => !!code && !KNOWN_PIPELINE_CODES.includes(code as typeof KNOWN_PIPELINE_CODES[number]))
      )
    );

    const unknown: PipelineRow[] = unknownCodes.map(code => ({
      pipelineCode: code,
      labelKey: code,
      isKnown: false,
      run: this.pickLatestRun(allRuns, code),
    }));

    return [...known, ...unknown];
  });

  // Matches the document's documentTypeCode against any registered DOCUMENT_TYPE_HANDLER
  // AND filters out handlers whose permissionPolicy the current user lacks. When a
  // business module (Contracts, Invoices, ...) registers a handler via host wiring,
  // the "Open in module" button becomes visible iff the document type matches AND the
  // user can actually use the destination route — avoids ghost buttons that lead to 403.
  matchingTypeHandler = computed(() => {
    const code = this.document()?.documentTypeCode;
    if (!code) return null;
    const match = this.typeHandlers.find(h => code.startsWith(h.documentTypeCodePrefix));
    if (!match) return null;
    if (match.permissionPolicy && !this.permissionService.getGrantedPolicy(match.permissionPolicy)) {
      return null;
    }
    return match;
  });

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

          const lifecycleSettled =
            doc.lifecycleStatus === DocumentLifecycleStatus.Ready ||
            doc.lifecycleStatus === DocumentLifecycleStatus.Failed;
          const anyRunPending = (doc.pipelineRuns ?? []).some(r =>
            r.status === PipelineRunStatus.Pending || r.status === PipelineRunStatus.Running
          );

          if (lifecycleSettled && !anyRunPending) {
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

  setTab(tab: 'info' | 'relations'): void {
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

  openChat(): void {
    const doc = this.document();
    if (!doc?.id) return;

    this.router.navigate(['/chat'], {
      queryParams: {
        documentId: doc.id,
        documentTypeCode: doc.documentTypeCode || null,
        title: doc.fileOrigin?.originalFileName || doc.originalFileBlobName,
      },
    });
  }

  openInModule(): void {
    const handler = this.matchingTypeHandler();
    const doc = this.document();
    if (!handler || !doc) return;
    this.router.navigate(handler.buildRoute(doc.id) as unknown[]);
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

  getRunStatusBadgeClass(status: PipelineRunStatus | undefined): string {
    switch (status) {
      case PipelineRunStatus.Pending:   return 'badge bg-secondary';
      case PipelineRunStatus.Running:   return 'badge bg-warning text-dark';
      case PipelineRunStatus.Succeeded: return 'badge bg-success';
      case PipelineRunStatus.Failed:    return 'badge bg-danger';
      case PipelineRunStatus.Skipped:   return 'badge bg-light text-dark border';
      default:                          return 'badge bg-light text-muted border';
    }
  }

  getRunStatusLabel(status: PipelineRunStatus | undefined): string {
    switch (status) {
      case PipelineRunStatus.Pending:   return '::Document:Pipeline:Status:Pending';
      case PipelineRunStatus.Running:   return '::Document:Pipeline:Status:Running';
      case PipelineRunStatus.Succeeded: return '::Document:Pipeline:Status:Succeeded';
      case PipelineRunStatus.Failed:    return '::Document:Pipeline:Status:Failed';
      case PipelineRunStatus.Skipped:   return '::Document:Pipeline:Status:Skipped';
      default:                          return '::Document:Pipeline:Status:NotStarted';
    }
  }

  isRunInProgress(status: PipelineRunStatus | undefined): boolean {
    return status === PipelineRunStatus.Pending || status === PipelineRunStatus.Running;
  }

  isRetryable(run: DocumentPipelineRunDto | null | undefined): boolean {
    return !!run && run.status === PipelineRunStatus.Failed;
  }

  retryPipeline(pipelineCode: string): void {
    if (this.retryingPipeline() !== null) return;

    this.retryingPipeline.set(pipelineCode);
    this.documentService.retryPipeline(this.documentId, pipelineCode).subscribe({
      next: () => {
        this.retryingPipeline.set(null);
        this.toaster.success('::Document:Pipeline:RetryQueued', '::Success');
        if (!this.pollSubscription) {
          this.startPolling();
        }
      },
      error: () => {
        this.retryingPipeline.set(null);
        this.toaster.error('::Document:Pipeline:RetryFailed', '::Error');
      },
    });
  }

  getElapsedMs(run: DocumentPipelineRunDto): number | null {
    if (!run.startedAt) return null;
    const start = new Date(run.startedAt).getTime();
    if (Number.isNaN(start)) return null;
    const end = run.completedAt ? new Date(run.completedAt).getTime() : Date.now();
    if (Number.isNaN(end) || end < start) return null;
    return end - start;
  }

  formatElapsed(run: DocumentPipelineRunDto): string {
    const ms = this.getElapsedMs(run);
    if (ms == null) return '';
    if (ms < 1000) return `${ms} ms`;
    const seconds = ms / 1000;
    if (seconds < 60) return `${seconds.toFixed(1)} s`;
    const minutes = Math.floor(seconds / 60);
    const remSeconds = Math.round(seconds - minutes * 60);
    return `${minutes}m ${remSeconds}s`;
  }

  protected pickLatestRun(runs: DocumentPipelineRunDto[], pipelineCode: string): DocumentPipelineRunDto | null {
    const matches = runs.filter(r => r.pipelineCode === pipelineCode);
    if (matches.length === 0) return null;
    return matches.reduce((prev, curr) => (curr.attemptNumber > prev.attemptNumber ? curr : prev));
  }
}
