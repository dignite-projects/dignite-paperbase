// ─── Document models ───────────────────────────────────────────────────────

export enum DocumentReviewStatus {
  None = 0,
  PendingReview = 10,
  Reviewed = 20,
}

export enum DocumentLifecycleStatus {
  Uploaded = 10,
  Processing = 20,
  Ready = 30,
  Failed = 99,
}

export enum SourceType {
  Physical = 0,
  Digital = 1,
}

export interface FileOriginDto {
  uploadedAt: string;
  uploadedByUserId: string;
  uploadedByUserName: string;
  originalFileName?: string;
  contentType: string;
  fileSize: number;
  deviceInfo?: string;
  scannedAt?: string;
}

export interface DocumentPipelineRunDto {
  id: string;
  pipelineCode: string;
  attemptNumber: number;
  status: string;
  startedAt: string;
  completedAt?: string;
  resultCode?: string;
  metadata?: string;
}

export interface DocumentDto {
  id: string;
  tenantId?: string;
  originalFileBlobName: string;
  sourceType: SourceType;
  fileOrigin: FileOriginDto;
  documentTypeCode?: string;
  lifecycleStatus: DocumentLifecycleStatus;
  reviewStatus: DocumentReviewStatus;
  confidenceScore: number;
  hasEmbedding: boolean;
  extractedText?: string;
  creationTime: string;
  pipelineRuns: DocumentPipelineRunDto[];
}

export interface GetDocumentListInput {
  maxResultCount?: number;
  skipCount?: number;
  sorting?: string;
  lifecycleStatus?: number;
  documentTypeCode?: string;
  reviewStatus?: DocumentReviewStatus;
}

// ─── Q&A models ─────────────────────────────────────────────────────────────

export enum QaMode {
  Auto = 0,
  Rag = 1,
  FullText = 2,
}

export interface AskDocumentInput {
  question: string;
  mode?: QaMode;
}

export interface GlobalAskInput {
  question: string;
  documentTypeCode?: string;
}

export interface QaSourceDto {
  text: string;
  chunkIndex?: number;
}

export interface QaResultDto {
  answer: string;
  sources: QaSourceDto[];
  actualMode: string;
  isDegraded: boolean;
}

// ─── Document relation models ────────────────────────────────────────────────

export enum RelationSource {
  Manual = 1,
  AiSuggested = 2,
}

export interface DocumentRelationDto {
  id: string;
  sourceDocumentId: string;
  targetDocumentId: string;
  relationType: string;
  source: RelationSource;
  confidenceScore: number;
  creationTime: string;
}

export interface CreateDocumentRelationInput {
  sourceDocumentId: string;
  targetDocumentId: string;
  relationType: string;
}

// ─── Shared ─────────────────────────────────────────────────────────────────

export interface PagedResultDto<T> {
  totalCount: number;
  items: T[];
}
