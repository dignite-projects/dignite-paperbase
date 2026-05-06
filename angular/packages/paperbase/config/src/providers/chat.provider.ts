import { eLayoutType, RoutesService } from '@abp/ng.core';
import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { PAPERBASE_PERMISSIONS } from '@dignite/paperbase';

export function provideChat(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const routes = inject(RoutesService);
      routes.add([
        {
          path: '/chat',
          name: '::Menu:DocumentChat',
          iconClass: 'fas fa-comments',
          requiredPolicy: PAPERBASE_PERMISSIONS.Documents.Chat.Default,
          order: 3,
          layout: eLayoutType.application,
        },
      ]);
    }),
  ]);
}
