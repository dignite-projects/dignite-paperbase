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
  documentId: string;
  pipelineCode: string;
  attemptNumber: number;
  status: string;
  startedAt: string;
  completedAt?: string;
  statusMessage?: string;
  /**
   * 各 pipeline 的专属输出。约定 key：
   *  - "Candidates": ClassificationCandidate[] — 分类流水线 top-K 候选
   */
  extraProperties?: Record<string, unknown>;
}

export interface ClassificationCandidate {
  typeCode: string;
  confidenceScore: number;
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
  classificationConfidence: number;
  hasEmbedding: boolean;
  markdown?: string;
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

// ─── Document relation models ────────────────────────────────────────────────

export enum RelationSource {
  Manual = 1,
  AiSuggested = 2,
}

export interface DocumentRelationDto {
  id: string;
  sourceDocumentId: string;
  targetDocumentId: string;
  description: string;
  source: RelationSource;
  confidenceScore: number;
  creationTime: string;
}

export interface CreateDocumentRelationInput {
  sourceDocumentId: string;
  targetDocumentId: string;
  description: string;
}

// ─── Shared ─────────────────────────────────────────────────────────────────

export interface PagedResultDto<T> {
  totalCount: number;
  items: T[];
}
