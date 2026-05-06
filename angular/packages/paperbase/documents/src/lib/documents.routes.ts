import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from '@abp/ng.core';
import { PAPERBASE_PERMISSIONS } from '@dignite/paperbase';

export const DOCUMENTS_ROUTES: Routes = [
  {
    path: '',
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Default },
    loadComponent: () =>
      import('./document-list/document-list.component').then(c => c.DocumentListComponent),
  },
  {
    path: 'upload',
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Upload },
    loadComponent: () =>
      import('./document-upload/document-upload.component').then(c => c.DocumentUploadComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Default },
    loadComponent: () =>
      import('./document-detail/document-detail.component').then(c => c.DocumentDetailComponent),
  },
];
