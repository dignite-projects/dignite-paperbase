import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LocalizationPipe } from '@abp/ng.core';
import {
  DocumentRelationDto,
  DocumentRelationService,
  RelationSource,
} from '@dignite/paperbase';

@Component({
  selector: 'lib-document-relations',
  templateUrl: './document-relations.component.html',
  imports: [CommonModule, LocalizationPipe],
})
export class DocumentRelationsComponent implements OnInit {
  @Input() documentId!: string;

  private readonly relationService = inject(DocumentRelationService);

  readonly RelationSource = RelationSource;

  relations = signal<DocumentRelationDto[]>([]);
  isLoading = signal(false);
  confirmingId = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.relationService.getList(this.documentId).subscribe({
      next: items => {
        this.relations.set(items);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false),
    });
  }

  confirm(id: string): void {
    this.confirmingId.set(id);
    this.relationService.confirm(id).subscribe({
      next: updated => {
        this.relations.update(list =>
          list.map(r => (r.id === id ? updated : r))
        );
        this.confirmingId.set(null);
      },
      error: () => this.confirmingId.set(null),
    });
  }

  delete(id: string): void {
    this.relationService.delete(id).subscribe({
      next: () => this.relations.update(list => list.filter(r => r.id !== id)),
    });
  }

  truncateId(id: string): string {
    return id.substring(0, 8) + '…';
  }
}
