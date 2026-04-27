import { Component, Input, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LocalizationPipe } from '@abp/ng.core';
import { DocumentQaService } from '../../proxy/document-qa.service';
import { QaResultDto } from '../../proxy/models';

@Component({
  selector: 'app-document-qa-panel',
  templateUrl: './document-qa-panel.component.html',
  imports: [CommonModule, FormsModule, LocalizationPipe],
})
export class DocumentQaPanelComponent {
  @Input() documentId!: string;
  @Input() hasEmbedding = false;

  private readonly qaService = inject(DocumentQaService);

  question = signal('');
  isAsking = signal(false);
  result = signal<QaResultDto | null>(null);
  error = signal<string | null>(null);

  ask(): void {
    const q = this.question().trim();
    if (!q) return;

    this.isAsking.set(true);
    this.result.set(null);
    this.error.set(null);

    this.qaService.ask(this.documentId, { question: q }).subscribe({
      next: res => {
        this.result.set(res);
        this.isAsking.set(false);
      },
      error: () => {
        this.error.set('Document:QaError');
        this.isAsking.set(false);
      },
    });
  }
}
