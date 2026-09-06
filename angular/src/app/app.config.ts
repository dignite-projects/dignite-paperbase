import { provideAbpCore, withOptions } from '@abp/ng.core';
import { provideAbpOAuth } from '@abp/ng.oauth';
import { provideSettingManagementConfig } from '@abp/ng.setting-management/config';
import { provideFeatureManagementConfig } from '@abp/ng.feature-management';
import { provideAbpThemeShared,  } from '@abp/ng.theme.shared';
import { provideIdentityConfig } from '@abp/ng.identity/config';
import { provideAccountConfig } from '@abp/ng.account/config';
import { provideTenantManagementConfig } from '@abp/ng.tenant-management/config';
import { registerLocaleForEsBuild } from '@abp/ng.core/locale';
import { provideThemeLeptonX } from '@abp/ng.theme.lepton-x';
import { provideSideMenuLayout } from '@abp/ng.theme.lepton-x/layouts';
import { provideLogo, withEnvironmentOptions } from "@abp/ng.theme.shared";
import { ApplicationConfig } from '@angular/core';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideFlexFields } from '@dignite/ng.flex-fields';
import { provideCKEditorFieldType } from '@dignite/ng.flex-fields-ckeditor';
import { provideExtract } from '@dignite/ng.vault-extract/config';
import { provideTagsFieldType } from '@dignite/ng.vault-extract/documents';
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
    provideTenantManagementConfig(),
    provideAbpThemeShared(),
    provideThemeLeptonX(),
    provideSideMenuLayout(),
    provideLogo(withEnvironmentOptions(environment)),
    provideExtract(),
    // Registers the field-type designer/control/search/view components <ff-flex-field-*> dispatches
    // to. provideFlexFields() supplies the eight kernel built-ins (Text/Number/Boolean/DateTime/Select/
    // Tree/Matrix/Table, #625); the two bolt-ons after it add CKEditor (long text) and Vault Extract's
    // own Tags. Order matters only for same-name overrides, which neither bolt-on is. Vault Extract's
    // own backend only ever offers Text/Number/Boolean/DateTime/Select/CKEditor/Tags/Table for a new
    // field (IVaultExtractFieldTypeRegistry has no extension for Tree/Matrix) - registering all eight
    // kernel types here only means Tree/Matrix would render correctly if ever encountered, not that
    // they become choosable in the field designer, which is server-filtered.
    provideFlexFields(),
    provideCKEditorFieldType(),
    provideTagsFieldType(),
  ]
};
