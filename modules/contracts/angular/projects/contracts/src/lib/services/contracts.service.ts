import { RestService } from '@abp/ng.core';
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
}
