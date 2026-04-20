import { Component, inject } from '@angular/core';
import { PaperbaseService } from '../services/paperbase.service';

@Component({
  selector: 'lib-paperbase',
  template: ` <p>paperbase works!</p> `,
})
export class PaperbaseComponent {
  protected readonly service = inject(PaperbaseService);

  constructor() {
    this.service.sample().subscribe(console.log);
  }
}
