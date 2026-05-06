import { authGuard, permissionGuard, RouterOutletComponent } from '@abp/ng.core';
import { Routes } from '@angular/router';
import { CONTRACTS_PERMISSIONS } from './permissions';

export const CONTRACTS_ROUTES: Routes = [
  {
    path: '',
    component: RouterOutletComponent,
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: CONTRACTS_PERMISSIONS.Contracts.Default },
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
