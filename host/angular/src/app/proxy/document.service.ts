import { Injectable, inject } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { DocumentDto, GetDocumentListInput, PagedResultDto } from './models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly rest = inject(RestService);
  private readonly apiName = 'Default';
  private readonly basePath = '/api/paperbase/documents';

  get = (id: string): Observable<DocumentDto> =>
    this.rest.request<void, DocumentDto>(
      { method: 'GET', url: `${this.basePath}/${id}` },
      { apiName: this.apiName }
    );

  getList = (input: GetDocumentListInput): Observable<PagedResultDto<DocumentDto>> =>
    this.rest.request<void, PagedResultDto<DocumentDto>>(
      {
        method: 'GET',
        url: this.basePath,
        params: {
          maxResultCount: input.maxResultCount ?? 10,
          skipCount: input.skipCount ?? 0,
          sorting: input.sorting,
          lifecycleStatus: input.lifecycleStatus,
          documentTypeCode: input.documentTypeCode,
        },
      },
      { apiName: this.apiName }
    );

  upload = (file: File): Observable<DocumentDto> => {
    const formData = new FormData();
    formData.append('File', file, file.name);
    formData.append('FileName', file.name);
    return this.rest.request<FormData, DocumentDto>(
      {
        method: 'POST',
        url: `${this.basePath}/upload`,
        body: formData,
      },
      { apiName: this.apiName }
    );
  };

  delete = (id: string): Observable<void> =>
    this.rest.request<void, void>(
      { method: 'DELETE', url: `${this.basePath}/${id}` },
      { apiName: this.apiName }
    );

  getBlobUrl = (id: string): string =>
    `/api/paperbase/documents/${id}/blob`;
}
