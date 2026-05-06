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

export interface ClassificationCandidate {
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
  // Pipeline-specific outputs. Known keys:
  //  - "Candidates": ClassificationCandidate[] — top-K classification candidates
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
  markdown?: string;
  creationTime: string;
  pipelineRuns: DocumentPipelineRunDto[];
}

export interface GetDocumentListInput {
  maxResultCount?: number;
  skipCount?: number;
  sorting?: string;
  lifecycleStatus?: DocumentLifecycleStatus | number | null;
  documentTypeCode?: string | null;
  reviewStatus?: DocumentReviewStatus | null;
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
  summary?: string | null;
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
