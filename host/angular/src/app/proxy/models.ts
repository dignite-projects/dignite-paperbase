// ─── Document models ───────────────────────────────────────────────────────

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
  status: string;
  startedAt: string;
  completedAt?: string;
  resultCode?: string;
}

export interface DocumentDto {
  id: string;
  tenantId?: string;
  originalFileBlobName: string;
  sourceType: SourceType;
  fileOrigin: FileOriginDto;
  documentTypeCode?: string;
  lifecycleStatus: DocumentLifecycleStatus;
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
  lifecycleStatus?: string;
  documentTypeCode?: string;
}

// ─── Shared ─────────────────────────────────────────────────────────────────

export interface PagedResultDto<T> {
  totalCount: number;
  items: T[];
}
