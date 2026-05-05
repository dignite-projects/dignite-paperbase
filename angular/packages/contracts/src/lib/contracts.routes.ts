import { RouterOutletComponent } from '@abp/ng.core';
import { Routes } from '@angular/router';

export const CONTRACTS_ROUTES: Routes = [
  {
    path: '',
    component: RouterOutletComponent,
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./components/contracts.component').then(c => c.ContractsComponent),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./components/contract-detail.component').then(c => c.ContractDetailComponent),
      },
    ],
  },
];
