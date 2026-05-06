import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { LocalizationPipe } from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { DocumentService } from '@dignite/paperbase';

interface FileUploadState {
  name: string;
  done: boolean;
  error: boolean;
}

@Component({
  selector: 'lib-document-upload',
  templateUrl: './document-upload.component.html',
  styleUrls: ['./document-upload.component.scss'],
  imports: [CommonModule, LocalizationPipe],
})
export class DocumentUploadComponent {
  private readonly documentService = inject(DocumentService);
  private readonly router = inject(Router);
  private readonly toaster = inject(ToasterService);

  isDragOver = signal(false);
  isUploading = signal(false);
  uploadingFiles = signal<FileUploadState[]>([]);

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.uploadFiles(Array.from(files));
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.uploadFiles(Array.from(input.files));
      input.value = '';
    }
  }

  private uploadFiles(files: File[]): void {
    const allowedTypes = ['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'application/pdf'];
    const maxSizeBytes = 20 * 1024 * 1024;

    const valid = files.filter(f => {
      if (!allowedTypes.includes(f.type)) {
        this.toaster.error('::Document:UnsupportedFileType', '::Error');
        return false;
      }
      if (f.size > maxSizeBytes) {
        this.toaster.error('::Document:FileTooLarge', '::Error');
        return false;
      }
      return true;
    });

    if (valid.length === 0) return;

    this.isUploading.set(true);
    this.uploadingFiles.set(valid.map(f => ({ name: f.name, done: false, error: false })));

    let completed = 0;
    const onAllDone = () => {
      this.isUploading.set(false);
      const hasError = this.uploadingFiles().some(f => f.error);
      if (hasError) {
        this.toaster.warn('::Document:SomeUploadsFailed', '::Warning');
      } else {
        this.toaster.success('::Document:UploadedSuccessfully', '::Success');
      }
      this.router.navigate(['/documents']);
    };

    for (let i = 0; i < valid.length; i++) {
      const file = valid[i];
      const idx = i;
      this.documentService.upload(file).subscribe({
        next: () => {
          this.uploadingFiles.update(list =>
            list.map((item, j) => j === idx ? { ...item, done: true } : item)
          );
          if (++completed === valid.length) onAllDone();
        },
        error: () => {
          this.uploadingFiles.update(list =>
            list.map((item, j) => j === idx ? { ...item, error: true } : item)
          );
          if (++completed === valid.length) onAllDone();
        },
      });
    }
  }
}
