import { InjectionToken } from '@angular/core';

export interface DocumentTypeHandler {
  // Matched against DocumentDto.documentTypeCode via String.startsWith. Backend
  // convention is to namespace business module document types by a trailing-dot
  // prefix (e.g., "contract." → "contract.general", "contract.amendment").
  documentTypeCodePrefix: string;
  labelKey: string;
  iconClass: string;
  buildRoute(documentId: string): unknown[];
  permissionPolicy?: string;
}

export const DOCUMENT_TYPE_HANDLER = new InjectionToken<DocumentTypeHandler[]>(
  'DOCUMENT_TYPE_HANDLER',
);
