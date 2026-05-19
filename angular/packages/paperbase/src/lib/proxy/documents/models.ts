import type { EntityDto, ExtensibleObject } from '@abp/ng.core';
import type { SourceType } from './source-type.enum';
import type { DocumentLifecycleStatus } from './document-lifecycle-status.enum';
import type { DocumentReviewStatus } from './document-review-status.enum';
import type { PipelineRunStatus } from './pipeline-run-status.enum';
import type { RelationSource } from './relation-source.enum';

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

// Mirrors C# Dignite.Paperbase.Documents.PipelineRunCandidate (Domain.Shared).
export interface PipelineRunCandidate {
  typeCode: string;
  confidenceScore: number;
}

export interface DocumentPipelineRunDto extends ExtensibleObject {
  id: string;
  documentId: string;
  pipelineCode: string;
  attemptNumber: number;
  status: PipelineRunStatus;
  startedAt: string;
  completedAt?: string;
  statusMessage?: string;
  // Top-K classification candidates surfaced from the low-confidence path —
  // strong-typed projection of ExtraProperties["Candidates"]. null when there
  // is no low-confidence outcome to review. Prefer this over reading
  // extraProperties by key.
  candidates?: PipelineRunCandidate[] | null;
  extraProperties?: Record<string, unknown>;
}

export interface DocumentDto extends EntityDto<string> {
  tenantId?: string;
  originalFileBlobName: string;
  sourceType: SourceType;
  fileOrigin: FileOriginDto;
  documentTypeCode?: string;
  lifecycleStatus: DocumentLifecycleStatus;
  reviewStatus: DocumentReviewStatus;
  classificationConfidence: number;
  classificationReason?: string | null;
  hasEmbedding: boolean;
  // Display title generated from extracted Markdown (text extraction pipeline).
  // Pre-migration documents may be null — UI must fall back to fileOrigin.originalFileName.
  title?: string | null;
  markdown?: string;
  creationTime: string;
  pipelineRuns: DocumentPipelineRunDto[];
  // 软删除时间（仅当 isDeleted=true 的列表查询时有值）。
  deletionTime?: string | null;
}

export interface GetDocumentListInput {
  maxResultCount?: number;
  skipCount?: number;
  sorting?: string;
  lifecycleStatus?: DocumentLifecycleStatus | number | null;
  documentTypeCode?: string | null;
  reviewStatus?: DocumentReviewStatus | null;
  // true = 仅返回已软删除文档（回收站视图）；undefined/false = 仅返回未删除文档
  isDeleted?: boolean | null;
}

export interface DocumentRelationDto extends EntityDto<string> {
  sourceDocumentId: string;
  targetDocumentId: string;
  description: string;
  source: RelationSource;
  confidence?: number | null;
  creationTime: string;
}

export interface CreateDocumentRelationInput {
  sourceDocumentId: string;
  targetDocumentId: string;
  description: string;
}

export interface DocumentRelationEdgeDto {
  id?: string;
  sourceDocumentId?: string;
  targetDocumentId?: string;
  description?: string;
  source?: RelationSource;
  confidence?: number | null;
}

export interface DocumentRelationNodeDto {
  documentId?: string;
  title?: string | null;
  documentTypeCode?: string | null;
  lifecycleStatus?: DocumentLifecycleStatus;
  reviewStatus?: DocumentReviewStatus;
  distance?: number;
}

export interface DocumentRelationGraphDto {
  rootDocumentId?: string;
  nodes?: DocumentRelationNodeDto[];
  edges?: DocumentRelationEdgeDto[];
}

export interface GetDocumentRelationGraphInput {
  rootDocumentId: string;
  depth?: number;
  includeAiSuggested?: boolean;
}
