import { Injectable, inject } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import type {
  CreateDocumentRelationInput,
  DocumentRelationDto,
  DocumentRelationGraphDto,
  GetDocumentRelationGraphInput,
} from '../../documents/models';

@Injectable({ providedIn: 'root' })
export class DocumentRelationService {
  private readonly rest = inject(RestService);
  private readonly apiName = 'Default';
  private readonly basePath = '/api/paperbase/document-relations';

  getList = (documentId: string): Observable<DocumentRelationDto[]> =>
    this.rest.request<void, DocumentRelationDto[]>(
      { method: 'GET', url: this.basePath, params: { documentId } },
      { apiName: this.apiName }
    );

  create = (input: CreateDocumentRelationInput): Observable<DocumentRelationDto> =>
    this.rest.request<CreateDocumentRelationInput, DocumentRelationDto>(
      { method: 'POST', url: this.basePath, body: input },
      { apiName: this.apiName }
    );

  confirm = (id: string): Observable<DocumentRelationDto> =>
    this.rest.request<void, DocumentRelationDto>(
      { method: 'POST', url: `${this.basePath}/${id}/confirm` },
      { apiName: this.apiName }
    );

  delete = (id: string): Observable<void> =>
    this.rest.request<void, void>(
      { method: 'DELETE', url: `${this.basePath}/${id}` },
      { apiName: this.apiName }
    );

  getGraph = (input: GetDocumentRelationGraphInput): Observable<DocumentRelationGraphDto> =>
    this.rest.request<void, DocumentRelationGraphDto>(
      {
        method: 'GET',
        url: `${this.basePath}/graph`,
        params: {
          rootDocumentId: input.rootDocumentId,
          depth: input.depth,
          includeAiSuggested: input.includeAiSuggested,
        },
      },
      { apiName: this.apiName }
    );
}
