import { RestService, Rest } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import type { GlobalAskInput, QaResultDto } from '../../documents/models';

@Injectable({
  providedIn: 'root',
})
export class DocumentQaService {
  private restService = inject(RestService);
  apiName = 'Default';
  

  globalAsk = (input: GlobalAskInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, QaResultDto>({
      method: 'POST',
      url: '/api/paperbase/document-qa/global-ask',
      body: input,
    },
    { apiName: this.apiName,...config });
}