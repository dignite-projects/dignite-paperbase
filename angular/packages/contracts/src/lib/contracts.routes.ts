import { RouterOutletComponent } from '@abp/ng.core';
import { Routes } from '@angular/router';

export const CONTRACTS_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    component: RouterOutletComponent,
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./components/contracts.component').then(c => c.ContractsComponent),
      },
    ],
  },
];
