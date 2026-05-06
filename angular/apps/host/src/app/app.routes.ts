import { Routes } from '@angular/router';

export const APP_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./home/home.component').then(c => c.HomeComponent),
  },
  {
    path: 'account',
    loadChildren: () => import('@abp/ng.account').then(c => c.createRoutes()),
  },
  {
    path: 'identity',
    loadChildren: () => import('@abp/ng.identity').then(c => c.createRoutes()),
  },
  {
    path: 'setting-management',
    loadChildren: () => import('@abp/ng.setting-management').then(c => c.createRoutes()),
  },
  {
    path: 'documents',
    loadChildren: () => import('@dignite/paperbase/documents').then(m => m.DOCUMENTS_ROUTES),
  },
  {
    path: 'chat',
    loadChildren: () => import('@dignite/paperbase/chat').then(m => m.CHAT_ROUTES),
  },
  {
    path: 'contracts',
    loadChildren: () => import('@dignite/paperbase.contracts').then(m => m.CONTRACTS_ROUTES),
  },
];
