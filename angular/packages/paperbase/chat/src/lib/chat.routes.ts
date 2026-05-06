import { authGuard, permissionGuard } from '@abp/ng.core';
import { Routes } from '@angular/router';
import { PAPERBASE_PERMISSIONS } from '@dignite/paperbase';

export const CHAT_ROUTES: Routes = [
  {
    path: '',
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Chat.Default },
    loadComponent: () => import('./chat.component').then(c => c.ChatComponent),
  },
];
