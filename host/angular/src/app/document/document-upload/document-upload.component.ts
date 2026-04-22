import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { LocalizationPipe } from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { DocumentService } from '../../proxy/document.service';

@Component({
  selector: 'app-document-upload',
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
  uploadProgress = signal<string | null>(null);

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
      this.uploadFile(files[0]);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.uploadFile(input.files[0]);
      input.value = '';
    }
  }

  private uploadFile(file: File): void {
    const allowedTypes = ['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'application/pdf'];
    if (!allowedTypes.includes(file.type)) {
      this.toaster.error('::Document:UnsupportedFileType', '::Error');
      return;
    }

    const maxSizeMb = 20;
    if (file.size > maxSizeMb * 1024 * 1024) {
      this.toaster.error('::Document:FileTooLarge', '::Error');
      return;
    }

    this.isUploading.set(true);
    this.uploadProgress.set(file.name);

    this.documentService.upload(file).subscribe({
      next: doc => {
        this.isUploading.set(false);
        this.uploadProgress.set(null);
        this.toaster.success('::Document:UploadedSuccessfully', '::Success');
        this.router.navigate(['/documents', doc.id]);
      },
      error: () => {
        this.isUploading.set(false);
        this.uploadProgress.set(null);
        this.toaster.error('::Document:UploadFailed', '::Error');
      },
    });
  }
}
