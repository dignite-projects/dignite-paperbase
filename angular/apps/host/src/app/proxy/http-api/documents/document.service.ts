import { RestService, Rest } from '@abp/ng.core';
import type { PagedResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import type { ConfirmClassificationInput, DocumentDto, DocumentListItemDto, GetDocumentListInput, UploadDocumentInput } from '../../documents/models';

@Injectable({
  providedIn: 'root',
})
export class DocumentService {
  private restService = inject(RestService);
  apiName = 'Default';


  confirmClassification = (id: string, input: ConfirmClassificationInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentDto>({
      method: 'POST',
      url: `/api/paperbase/documents/${id}/confirm-classification`,
      body: input,
    },
    { apiName: this.apiName,...config });


  delete = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'DELETE',
      url: `/api/paperbase/documents/${id}`,
    },
    { apiName: this.apiName,...config });


  get = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentDto>({
      method: 'GET',
      url: `/api/paperbase/documents/${id}`,
    },
    { apiName: this.apiName,...config });


  getBlob = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, Blob>({
      method: 'GET',
      responseType: 'blob',
      url: `/api/paperbase/documents/${id}/blob`,
    },
    { apiName: this.apiName,...config });


  getExport = (input: GetDocumentListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, Blob>({
      method: 'GET',
      responseType: 'blob',
      url: '/api/paperbase/documents/export',
      params: { lifecycleStatus: input.lifecycleStatus, documentTypeCode: input.documentTypeCode, reviewStatus: input.reviewStatus, sorting: input.sorting, skipCount: input.skipCount, maxResultCount: input.maxResultCount },
    },
    { apiName: this.apiName,...config });


  getList = (input: GetDocumentListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, PagedResultDto<DocumentListItemDto>>({
      method: 'GET',
      url: '/api/paperbase/documents',
      params: { lifecycleStatus: input.lifecycleStatus, documentTypeCode: input.documentTypeCode, reviewStatus: input.reviewStatus, sorting: input.sorting, skipCount: input.skipCount, maxResultCount: input.maxResultCount },
    },
    { apiName: this.apiName,...config });


  restore = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'POST',
      url: `/api/paperbase/documents/${id}/restore`,
    },
    { apiName: this.apiName,...config });


  upload = (input: UploadDocumentInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, DocumentDto>({
      method: 'POST',
      url: '/api/paperbase/documents/upload',
      body: input.file,
    },
    { apiName: this.apiName,...config });
}
