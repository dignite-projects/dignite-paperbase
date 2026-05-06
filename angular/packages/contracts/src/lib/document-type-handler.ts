// Plain handler config consumed by host's app.config.ts when wiring DOCUMENT_TYPE_HANDLER.
// Kept as a value-only module to avoid contracts importing from @dignite/paperbase
// (the cross-package source-level import has a known ng-packagr / Angular partial
// compilation issue that surfaces as 'Cannot destructure property pos of file.referencedFiles').
import { CONTRACTS_PERMISSIONS } from './permissions';

export const CONTRACTS_DOCUMENT_TYPE_HANDLER = {
  documentTypeCodePrefix: 'contract.',
  labelKey: '::Contract:OpenInModule',
  iconClass: 'fas fa-file-contract',
  buildRoute: () => ['/contracts'] as unknown[],
  permissionPolicy: CONTRACTS_PERMISSIONS.Contracts.Default,
} as const;
