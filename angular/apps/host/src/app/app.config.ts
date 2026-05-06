import { provideAbpCore, withOptions } from '@abp/ng.core';
import { provideAbpOAuth } from '@abp/ng.oauth';
import { provideSettingManagementConfig } from '@abp/ng.setting-management/config';
import { provideFeatureManagementConfig } from '@abp/ng.feature-management';
import { provideAbpThemeShared,  } from '@abp/ng.theme.shared';
import { provideIdentityConfig } from '@abp/ng.identity/config';
import { provideAccountConfig } from '@abp/ng.account/config';
import { registerLocaleForEsBuild } from '@abp/ng.core/locale';
import { provideThemeLeptonX } from '@abp/ng.theme.lepton-x';
import { provideSideMenuLayout } from '@abp/ng.theme.lepton-x/layouts';
import { provideLogo, withEnvironmentOptions } from "@abp/ng.theme.shared";
import { ApplicationConfig } from '@angular/core';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { DOCUMENT_TYPE_HANDLER } from '@dignite/paperbase';
import { provideChat, provideDocuments } from '@dignite/paperbase/config';
import { CONTRACTS_DOCUMENT_TYPE_HANDLER } from '@dignite/paperbase.contracts';
import { provideContracts } from '@dignite/paperbase.contracts/config';
import { environment } from '../environments/environment';
import { APP_ROUTES } from './app.routes';
import { HOME_MENU_PROVIDER } from './home/home.menu.provider';
import { FOOTER_PROVIDER } from './footer/footer.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(APP_ROUTES),
    HOME_MENU_PROVIDER,
    FOOTER_PROVIDER,
    provideAnimations(),
    provideAbpCore(
      withOptions({
        environment,
        registerLocaleFn: registerLocaleForEsBuild(),
      }),
    ),
    provideAbpOAuth(),
    provideIdentityConfig(),
    provideSettingManagementConfig(),
    provideFeatureManagementConfig(),
    provideAccountConfig(),
    provideAbpThemeShared(),
    provideThemeLeptonX(),
    provideSideMenuLayout(),
    provideLogo(withEnvironmentOptions(environment)),
    provideChat(),
    provideDocuments(),
    provideContracts(),
    // Each business module exports its DOCUMENT_TYPE_HANDLER value (the cross-link
    // metadata that lights up the "Open in module" button on document-detail). Host
    // wires it into the multi-token here — provideContracts() can't do it inside
    // contracts/config because cross-package source-level imports of the token from
    // @dignite/paperbase trip an ng-packagr / Angular partial-compilation edge case.
    // For each new business module (invoices, receipts, ...): add a similar line.
    { provide: DOCUMENT_TYPE_HANDLER, multi: true, useValue: CONTRACTS_DOCUMENT_TYPE_HANDLER },
  ]
};
