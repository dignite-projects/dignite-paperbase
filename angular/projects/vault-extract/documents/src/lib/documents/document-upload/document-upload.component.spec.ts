import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LocalizationService, PermissionService } from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  CabinetService,
  DocumentTypeService,
  DocumentUploadService,
  EXTRACT_PERMISSIONS,
} from '@dignite/ng.vault-extract';
import { DocumentUploadComponent } from './document-upload.component';

// #623: the "declared document type" selector is an operator-confirmation shortcut that
// skips LLM classification on the backend (ConfirmClassification semantics), so it must
// only render for callers holding that permission, and the selection must flow through to
// every file in a batch upload.

const DOCUMENT_TYPES = [
  { id: 'type-1', displayName: 'Contract' },
  { id: 'type-2', displayName: 'Invoice' },
];

function fakeFile(name = 'contract.pdf'): File {
  return new File(['content'], name, { type: 'application/pdf' });
}

async function setup(grantedPolicies: Set<string>) {
  const uploadSpy = vi.fn().mockReturnValue(of({ id: 'doc-1' }));

  await TestBed.configureTestingModule({
    imports: [DocumentUploadComponent],
    providers: [
      provideRouter([]),
      {
        provide: PermissionService,
        useValue: { getGrantedPolicy: (key: string) => grantedPolicies.has(key) },
      },
      { provide: LocalizationService, useValue: { instant: (key: string) => key } },
      { provide: ToasterService, useValue: { success: vi.fn(), error: vi.fn() } },
      { provide: CabinetService, useValue: { getList: () => of([]) } },
      { provide: DocumentTypeService, useValue: { getVisible: () => of(DOCUMENT_TYPES) } },
      { provide: DocumentUploadService, useValue: { upload: uploadSpy } },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<DocumentUploadComponent> = TestBed.createComponent(
    DocumentUploadComponent,
  );
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, uploadSpy };
}

describe('DocumentUploadComponent — declared document type (#623)', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('hides the selector and loads no types without ConfirmClassification', async () => {
    const { fixture } = await setup(new Set([EXTRACT_PERMISSIONS.Documents.Upload]));
    const component = fixture.componentInstance;

    expect(component.canDeclareType).toBe(false);
    expect(component.documentTypes()).toEqual([]);
    expect(fixture.nativeElement.querySelector('.document-type-select')).toBeNull();
  });

  it('shows the selector and loads visible types with ConfirmClassification', async () => {
    const { fixture } = await setup(
      new Set([
        EXTRACT_PERMISSIONS.Documents.Upload,
        EXTRACT_PERMISSIONS.Documents.ConfirmClassification,
      ]),
    );
    const component = fixture.componentInstance;

    expect(component.canDeclareType).toBe(true);
    expect(component.documentTypes()).toEqual(DOCUMENT_TYPES);
    expect(fixture.nativeElement.querySelector('.document-type-select')).not.toBeNull();
  });

  it('passes the selected DocumentTypeId through to every file in a batch upload', async () => {
    const { fixture, uploadSpy } = await setup(
      new Set([
        EXTRACT_PERMISSIONS.Documents.Upload,
        EXTRACT_PERMISSIONS.Documents.ConfirmClassification,
      ]),
    );
    const component = fixture.componentInstance;
    component.selectedDocumentTypeId.set('type-1');

    (component as any).uploadFiles([fakeFile('a.pdf'), fakeFile('b.pdf')]);

    expect(uploadSpy).toHaveBeenCalledTimes(2);
    expect(uploadSpy).toHaveBeenNthCalledWith(1, expect.any(File), undefined, 'type-1');
    expect(uploadSpy).toHaveBeenNthCalledWith(2, expect.any(File), undefined, 'type-1');
  });

  it('does not send a DocumentTypeId when no type is declared', async () => {
    const { fixture, uploadSpy } = await setup(
      new Set([
        EXTRACT_PERMISSIONS.Documents.Upload,
        EXTRACT_PERMISSIONS.Documents.ConfirmClassification,
      ]),
    );
    const component = fixture.componentInstance;

    (component as any).uploadFiles([fakeFile('a.pdf')]);

    expect(uploadSpy).toHaveBeenCalledWith(expect.any(File), undefined, undefined);
  });
});
