import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';

@Injectable({
  providedIn: 'root',
})
export class PaperbaseService {
  apiName = 'Paperbase';

  private restService = inject(RestService);

  sample() {
    return this.restService.request<void, any>(
      { method: 'GET', url: '/api/paperbase/example' },
      { apiName: this.apiName }
    );
  }
}
