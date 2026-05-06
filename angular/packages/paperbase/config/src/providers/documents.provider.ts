import { eLayoutType, RoutesService } from '@abp/ng.core';
import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { PAPERBASE_PERMISSIONS } from '@dignite/paperbase';

export function provideDocuments(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const routes = inject(RoutesService);
      routes.add([
        {
          path: '/documents',
          name: '::Menu:Documents',
          iconClass: 'fas fa-file-alt',
          requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Default,
          order: 2,
          layout: eLayoutType.application,
        },
        {
          path: '/documents',
          name: '::Menu:DocumentList',
          iconClass: 'fas fa-list',
          parentName: '::Menu:Documents',
          requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Default,
          order: 1,
          layout: eLayoutType.application,
        },
        {
          path: '/documents/upload',
          name: '::Menu:UploadDocument',
          iconClass: 'fas fa-upload',
          parentName: '::Menu:Documents',
          requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Upload,
          order: 2,
          layout: eLayoutType.application,
        },
      ]);
    }),
  ]);
}
