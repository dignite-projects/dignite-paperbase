import { describe, expect, it } from 'vitest';
import { buildUploadFormData } from './document-upload.service';

// #623: declaring a DocumentTypeId at upload is equivalent to an operator confirming
// classification (skips LLM classification entirely on the backend), so the form field
// must be appended only when the caller actually selected a type — never a stray empty
// value that would trip the backend's additive ConfirmClassification permission check.

function fakeFile(name = 'contract.pdf'): File {
  return new File(['content'], name, { type: 'application/pdf' });
}

describe('buildUploadFormData', () => {
  it('appends DocumentTypeId when a type is declared', () => {
    const formData = buildUploadFormData(fakeFile(), undefined, 'a1b2c3');

    expect(formData.get('DocumentTypeId')).toBe('a1b2c3');
  });

  it('does not append DocumentTypeId when none is declared (undefined)', () => {
    const formData = buildUploadFormData(fakeFile());

    expect(formData.has('DocumentTypeId')).toBe(false);
  });

  it('does not append DocumentTypeId for an empty-string selection (the "let AI classify" default)', () => {
    const formData = buildUploadFormData(fakeFile(), undefined, '');

    expect(formData.has('DocumentTypeId')).toBe(false);
  });

  it('appends both CabinetId and DocumentTypeId together when both are set', () => {
    const formData = buildUploadFormData(fakeFile(), 'cab-1', 'type-1');

    expect(formData.get('CabinetId')).toBe('cab-1');
    expect(formData.get('DocumentTypeId')).toBe('type-1');
  });

  it('always appends the File field', () => {
    const formData = buildUploadFormData(fakeFile('scan.png'));

    const file = formData.get('File') as File;
    expect(file).toBeInstanceOf(File);
    expect(file.name).toBe('scan.png');
  });
});
