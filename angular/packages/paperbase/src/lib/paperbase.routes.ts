import { RouterOutletComponent } from '@abp/ng.core';
import { Routes } from '@angular/router';

export const PAPERBASE_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    component: RouterOutletComponent,
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./components/paperbase.component').then(c => c.PaperbaseComponent),
      },
    ],
  },
];
