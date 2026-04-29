import { EnvironmentService, RestService } from '@abp/ng.core';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ContractDto {
  id: string;
  documentId: string;
  documentTypeCode: string;
  title?: string;
  contractNumber?: string;
  partyAName?: string;
  partyBName?: string;
  counterpartyName?: string;
  signedDate?: string;
  effectiveDate?: string;
  expirationDate?: string;
  totalAmount?: number;
  currency?: string;
  status: ContractStatus;
  extractionConfidence?: number;
  needsReview: boolean;
}

export enum ContractStatus {
  Draft = 0,
  Active = 1,
  Expired = 2,
  Terminated = 3,
  Archived = 4,
}

export interface GetContractListInput {
  skipCount?: number;
  maxResultCount?: number;
  sorting?: string;
  documentId?: string;
  counterpartyKeyword?: string;
  expirationDateFrom?: string;
  expirationDateTo?: string;
  needsReview?: boolean;
  amountMin?: number;
  amountMax?: number;
}

export interface PagedResultDto<T> {
  items: T[];
  totalCount: number;
}

@Injectable({
  providedIn: 'root',
})
export class ContractsService {
  private readonly apiName = 'Contracts';

  private readonly restService = inject(RestService);
  private readonly env = inject(EnvironmentService);

  getList(input: GetContractListInput): Observable<PagedResultDto<ContractDto>> {
    return this.restService.request<void, PagedResultDto<ContractDto>>(
      {
        method: 'GET',
        url: '/api/paperbase/contracts',
        params: input as Record<string, string | number | boolean | undefined>,
      },
      { apiName: this.apiName }
    );
  }

  get(id: string): Observable<ContractDto> {
    return this.restService.request<void, ContractDto>(
      {
        method: 'GET',
        url: `/api/paperbase/contracts/${id}`,
      },
      { apiName: this.apiName }
    );
  }

  confirm(id: string): Observable<void> {
    return this.restService.request<void, void>(
      {
        method: 'POST',
        url: `/api/paperbase/contracts/${id}/confirm`,
      },
      { apiName: this.apiName }
    );
  }

  getExportUrl(input?: GetContractListInput): string {
    const params = new URLSearchParams();
    if (input?.counterpartyKeyword) params.set('counterpartyKeyword', input.counterpartyKeyword);
    if (input?.expirationDateFrom) params.set('expirationDateFrom', input.expirationDateFrom);
    if (input?.expirationDateTo) params.set('expirationDateTo', input.expirationDateTo);
    if (input?.amountMin != null) params.set('totalAmountMin', String(input.amountMin));
    if (input?.amountMax != null) params.set('totalAmountMax', String(input.amountMax));
    const qs = params.toString();
    return `${this.env.getApiUrl(undefined)}/api/paperbase/contracts/export${qs ? '?' + qs : ''}`;
  }
}
