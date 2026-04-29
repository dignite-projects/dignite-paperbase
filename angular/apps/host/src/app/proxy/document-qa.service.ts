import { Injectable, inject } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { AskDocumentInput, GlobalAskInput, QaResultDto } from './models';

@Injectable({ providedIn: 'root' })
export class DocumentQaService {
  private readonly rest = inject(RestService);
  private readonly apiName = 'Default';

  ask = (id: string, input: AskDocumentInput): Observable<QaResultDto> =>
    this.rest.request<AskDocumentInput, QaResultDto>(
      { method: 'POST', url: `/api/paperbase/documents/${id}/ask`, body: input },
      { apiName: this.apiName }
    );

  globalAsk = (input: GlobalAskInput): Observable<QaResultDto> =>
    this.rest.request<GlobalAskInput, QaResultDto>(
      { method: 'POST', url: '/api/paperbase/document-qa/global-ask', body: input },
      { apiName: this.apiName }
    );
}
