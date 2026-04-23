import { Injectable, inject } from '@angular/core';
import { EnvironmentService, RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { BulkUploadResultDto, DocumentDto, GetDocumentListInput, PagedResultDto } from './models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly rest = inject(RestService);
  private readonly env = inject(EnvironmentService);
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

  bulkUpload = (files: File[]): Observable<BulkUploadResultDto[]> => {
    const formData = new FormData();
    for (const file of files) {
      formData.append('files', file, file.name);
    }
    return this.rest.request<FormData, BulkUploadResultDto[]>(
      {
        method: 'POST',
        url: `${this.basePath}/bulk-upload`,
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
    `${this.env.getApiUrl()}/api/paperbase/documents/${id}/blob`;

  getExportUrl = (input: GetDocumentListInput): string => {
    const params = new URLSearchParams();
    if (input.lifecycleStatus != null) params.set('lifecycleStatus', String(input.lifecycleStatus));
    if (input.documentTypeCode) params.set('documentTypeCode', input.documentTypeCode);
    const qs = params.toString();
    return `${this.env.getApiUrl()}${this.basePath}/export${qs ? '?' + qs : ''}`;
  };
}
