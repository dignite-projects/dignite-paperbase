import { eLayoutType, RoutesService } from '@abp/ng.core';
import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { ePaperbaseRouteNames } from '../enums/route-names';

export const PAPERBASE_ROUTE_PROVIDERS = [
  provideAppInitializer(() => {
    configureRoutes();
  }),
];

export function configureRoutes() {
  const routesService = inject(RoutesService);
  routesService.add([
    {
      path: '/paperbase',
      name: ePaperbaseRouteNames.Paperbase,
      iconClass: 'fas fa-book',
      layout: eLayoutType.application,
      order: 3,
    },
  ]);
}

const PAPERBASE_PROVIDERS: EnvironmentProviders[] = [...PAPERBASE_ROUTE_PROVIDERS];

export function providePaperbase() {
  return makeEnvironmentProviders(PAPERBASE_PROVIDERS);
}
