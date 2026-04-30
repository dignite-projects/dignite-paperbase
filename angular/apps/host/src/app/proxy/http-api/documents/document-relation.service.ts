import { RestService, Rest } from '@abp/ng.core';
import type { ListResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import type { CreateDocumentRelationInput, DocumentRelationDto, DocumentRelationGraphDto, GetDocumentRelationGraphInput } from '../../documents/models';

@Injectable({
  providedIn: 'root',
})
export class DocumentRelationService {
  private restService = inject(RestService);
  apiName = 'Default';


  confirm = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentRelationDto>({
      method: 'POST',
      url: `/api/paperbase/document-relations/${id}/confirm`,
    },
    { apiName: this.apiName,...config });


  create = (input: CreateDocumentRelationInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentRelationDto>({
      method: 'POST',
      url: '/api/paperbase/document-relations',
      body: input,
    },
    { apiName: this.apiName,...config });


  delete = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'DELETE',
      url: `/api/paperbase/document-relations/${id}`,
    },
    { apiName: this.apiName,...config });


  getGraph = (input: GetDocumentRelationGraphInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentRelationGraphDto>({
      method: 'GET',
      url: '/api/paperbase/document-relations/graph',
      params: { rootDocumentId: input.rootDocumentId, depth: input.depth, includeAiSuggested: input.includeAiSuggested },
    },
    { apiName: this.apiName,...config });


  getList = (documentId: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, ListResultDto<DocumentRelationDto>>({
      method: 'GET',
      url: '/api/paperbase/document-relations',
      params: { documentId },
    },
    { apiName: this.apiName,...config });
}
