import type { EntityDto, ExtensibleObject, PagedAndSortedResultRequestDto } from '@abp/ng.core';
import type { SourceType } from './source-type.enum';
import type { DocumentLifecycleStatus } from './document-lifecycle-status.enum';
import type { DocumentReviewStatus } from './document-review-status.enum';
import type { PipelineRunStatus } from './pipeline-run-status.enum';
import type { RelationSource } from './relation-source.enum';
import type { IRemoteStreamContent } from '../volo/abp/content/models';

export interface ConfirmClassificationInput {
  documentTypeCode: string;
}

export interface CreateDocumentRelationInput {
  sourceDocumentId?: string;
  targetDocumentId?: string;
  description: string;
}

export interface DocumentDto extends EntityDto<string> {
  tenantId?: string | null;
  originalFileBlobName?: string;
  sourceType?: SourceType;
  fileOrigin?: FileOriginDto;
  documentTypeCode?: string | null;
  lifecycleStatus?: DocumentLifecycleStatus;
  reviewStatus?: DocumentReviewStatus;
  classificationConfidence?: number;
  classificationReason?: string | null;
  hasEmbedding?: boolean;
  markdown?: string | null;
  creationTime?: string;
  pipelineRuns?: DocumentPipelineRunDto[];
}

export interface DocumentListItemDto extends EntityDto<string> {
  tenantId?: string | null;
  originalFileBlobName?: string;
  sourceType?: SourceType;
  fileOrigin?: FileOriginDto;
  documentTypeCode?: string | null;
  lifecycleStatus?: DocumentLifecycleStatus;
  reviewStatus?: DocumentReviewStatus;
  classificationConfidence?: number;
  hasEmbedding?: boolean;
  creationTime?: string;
}

export interface DocumentPipelineRunDto extends ExtensibleObject {
  id?: string;
  documentId?: string;
  pipelineCode?: string;
  status?: PipelineRunStatus;
  attemptNumber?: number;
  startedAt?: string;
  completedAt?: string | null;
  statusMessage?: string | null;
}

export interface DocumentRelationDto extends EntityDto<string> {
  sourceDocumentId?: string;
  targetDocumentId?: string;
  description?: string;
  source?: RelationSource;
  confidence?: number | null;
  creationTime?: string;
}

export interface DocumentRelationEdgeDto {
  id?: string;
  sourceDocumentId?: string;
  targetDocumentId?: string;
  description?: string;
  source?: RelationSource;
  confidence?: number | null;
}

export interface DocumentRelationGraphDto {
  rootDocumentId?: string;
  nodes?: DocumentRelationNodeDto[];
  edges?: DocumentRelationEdgeDto[];
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

export interface FileOriginDto {
  uploadedByUserName?: string;
  originalFileName?: string | null;
  contentType?: string;
  fileSize?: number;
}

export interface GetDocumentListInput extends PagedAndSortedResultRequestDto {
  lifecycleStatus?: DocumentLifecycleStatus | null;
  documentTypeCode?: string | null;
  reviewStatus?: DocumentReviewStatus | null;
}

export interface GetDocumentRelationGraphInput {
  rootDocumentId: string;
  depth?: number;
  includeAiSuggested?: boolean;
}

export interface UploadDocumentInput {
  file: IRemoteStreamContent;
}
