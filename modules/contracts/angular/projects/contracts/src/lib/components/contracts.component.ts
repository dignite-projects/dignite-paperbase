import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe } from '@abp/ng.core';
import { finalize } from 'rxjs';
import { ContractDto, ContractStatus, ContractsService } from '../services/contracts.service';

@Component({
  selector: 'lib-contracts',
  imports: [CommonModule, FormsModule, LocalizationPipe],
  template: `
    <div class="container-fluid py-3">
      <div class="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <h1 class="h3 mb-0">{{ '::DocumentType:Contract' | abpLocalization }}</h1>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-success" (click)="exportCsv()">
            <i class="fa fa-download me-1"></i>
            {{ '::Contract:ExportCsv' | abpLocalization }}
          </button>
          <button type="button" class="btn btn-outline-primary" (click)="load()" [disabled]="loading">
            <i class="fa fa-refresh me-1"></i>
            {{ 'AbpUi::Refresh' | abpLocalization }}
          </button>
        </div>
      </div>

      <div class="row g-2 align-items-end mb-3">
        <div class="col-12 col-md-5 col-lg-4">
          <label class="form-label" for="counterpartyKeyword">
            {{ '::CounterpartyName' | abpLocalization }}
          </label>
          <input
            id="counterpartyKeyword"
            class="form-control"
            type="search"
            name="counterpartyKeyword"
            [(ngModel)]="counterpartyKeyword"
            (keyup.enter)="load()"
          />
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label" for="expirationDateFrom">
            {{ '::ExpirationDateFrom' | abpLocalization }}
          </label>
          <input
            id="expirationDateFrom"
            class="form-control"
            type="date"
            name="expirationDateFrom"
            [(ngModel)]="expirationDateFrom"
          />
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label" for="expirationDateTo">
            {{ '::ExpirationDateTo' | abpLocalization }}
          </label>
          <input
            id="expirationDateTo"
            class="form-control"
            type="date"
            name="expirationDateTo"
            [(ngModel)]="expirationDateTo"
          />
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label" for="amountMin">
            {{ '::TotalAmountMin' | abpLocalization }}
          </label>
          <input
            id="amountMin"
            class="form-control"
            type="number"
            name="amountMin"
            [(ngModel)]="amountMin"
            min="0"
          />
        </div>
        <div class="col-6 col-md-2">
          <label class="form-label" for="amountMax">
            {{ '::TotalAmountMax' | abpLocalization }}
          </label>
          <input
            id="amountMax"
            class="form-control"
            type="number"
            name="amountMax"
            [(ngModel)]="amountMax"
            min="0"
          />
        </div>
        <div class="col-12 col-md-auto">
          <div class="form-check mb-2">
            <input
              id="needsReview"
              class="form-check-input"
              type="checkbox"
              name="needsReview"
              [(ngModel)]="needsReviewOnly"
              (change)="load()"
            />
            <label class="form-check-label" for="needsReview">
              {{ '::NeedsReview' | abpLocalization }}
            </label>
          </div>
        </div>
        <div class="col-12 col-md-auto">
          <button type="button" class="btn btn-primary" (click)="load()" [disabled]="loading">
            <i class="fa fa-search me-1"></i>
            {{ 'AbpUi::Search' | abpLocalization }}
          </button>
        </div>
      </div>

      <div class="table-responsive">
        <table class="table table-hover align-middle mb-0">
          <thead>
            <tr>
              <th>{{ '::Title' | abpLocalization }}</th>
              <th>{{ '::CounterpartyName' | abpLocalization }}</th>
              <th>{{ '::SignedDate' | abpLocalization }}</th>
              <th>{{ '::ExpirationDate' | abpLocalization }}</th>
              <th class="text-end">{{ '::TotalAmount' | abpLocalization }}</th>
              <th>{{ '::Status' | abpLocalization }}</th>
              <th class="text-end">{{ '::Confidence' | abpLocalization }}</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngIf="loading">
              <td colspan="7" class="text-center py-4">
                <span class="spinner-border spinner-border-sm me-2"></span>
                {{ 'AbpUi::Loading' | abpLocalization }}
              </td>
            </tr>
            <tr *ngFor="let contract of contracts">
              <td>
                <div class="fw-semibold">{{ contract.title || '-' }}</div>
                <div class="text-muted small">{{ contract.contractNumber || contract.documentId }}</div>
              </td>
              <td>{{ contract.counterpartyName || '-' }}</td>
              <td>{{ contract.signedDate | date: 'yyyy-MM-dd' }}</td>
              <td>{{ contract.expirationDate | date: 'yyyy-MM-dd' }}</td>
              <td class="text-end">
                {{ contract.totalAmount == null ? '-' : (contract.totalAmount | number: '1.0-0') }}
                <span *ngIf="contract.totalAmount != null">{{ contract.currency || 'JPY' }}</span>
              </td>
              <td>
                <span class="badge" [ngClass]="statusClass(contract)">
                  {{ statusText(contract.status) }}
                </span>
                <span *ngIf="contract.needsReview" class="badge text-bg-warning ms-1">
                  {{ '::NeedsReview' | abpLocalization }}
                </span>
              </td>
              <td class="text-end">
                {{ contract.extractionConfidence == null ? '-' : (contract.extractionConfidence | percent: '1.0-0') }}
              </td>
            </tr>
            <tr *ngIf="!loading && contracts.length === 0">
              <td colspan="7" class="text-center text-muted py-4">
                {{ 'AbpUi::NoDataAvailable' | abpLocalization }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class ContractsComponent implements OnInit {
  protected readonly service = inject(ContractsService);
  protected contracts: ContractDto[] = [];
  protected counterpartyKeyword = '';
  protected expirationDateFrom = '';
  protected expirationDateTo = '';
  protected amountMin: number | null = null;
  protected amountMax: number | null = null;
  protected needsReviewOnly = false;
  protected loading = false;

  ngOnInit(): void {
    this.load();
  }

  protected exportCsv(): void {
    const url = this.service.getExportUrl({
      counterpartyKeyword: this.counterpartyKeyword || undefined,
      expirationDateFrom: this.expirationDateFrom || undefined,
      expirationDateTo: this.expirationDateTo || undefined,
      amountMin: this.amountMin ?? undefined,
      amountMax: this.amountMax ?? undefined,
    });
    window.open(url, '_blank');
  }

  protected load(): void {
    this.loading = true;
    this.service
      .getList({
        skipCount: 0,
        maxResultCount: 20,
        sorting: 'expirationDate',
        counterpartyKeyword: this.counterpartyKeyword || undefined,
        expirationDateFrom: this.expirationDateFrom || undefined,
        expirationDateTo: this.expirationDateTo || undefined,
        amountMin: this.amountMin ?? undefined,
        amountMax: this.amountMax ?? undefined,
        needsReview: this.needsReviewOnly ? true : undefined,
      })
      .pipe(finalize(() => (this.loading = false)))
      .subscribe(result => {
        this.contracts = result.items;
      });
  }

  protected statusText(status: ContractStatus): string {
    return ContractStatus[status] ?? '-';
  }

  protected statusClass(contract: ContractDto): string {
    if (contract.needsReview) {
      return 'text-bg-warning';
    }

    return contract.status === ContractStatus.Active ? 'text-bg-success' : 'text-bg-secondary';
  }
}
