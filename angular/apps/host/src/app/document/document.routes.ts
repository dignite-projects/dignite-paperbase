import { Routes } from '@angular/router';
import { authGuard } from '@abp/ng.core';

export const DOCUMENT_ROUTES: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./document-list/document-list.component').then(c => c.DocumentListComponent),
  },
  {
    path: 'upload',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./document-upload/document-upload.component').then(c => c.DocumentUploadComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./document-detail/document-detail.component').then(c => c.DocumentDetailComponent),
  },
];
