import { eLayoutType, RoutesService } from '@abp/ng.core';
import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import { eContractsRouteNames } from '../enums/route-names';

export const CONTRACTS_ROUTE_PROVIDERS = [
  provideAppInitializer(() => {
    configureRoutes();
  }),
];

export function configureRoutes() {
  const routesService = inject(RoutesService);
  routesService.add([
    {
      path: '/contracts',
      name: eContractsRouteNames.Contracts,
      iconClass: 'fas fa-book',
      layout: eLayoutType.application,
      order: 3,
    },
  ]);
}

const CONTRACTS_PROVIDERS: EnvironmentProviders[] = [...CONTRACTS_ROUTE_PROVIDERS];

export function provideContracts() {
  return makeEnvironmentProviders(CONTRACTS_PROVIDERS);
}
